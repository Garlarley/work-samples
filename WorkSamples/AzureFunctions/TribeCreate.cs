namespace MySamples.Azure
{
    public static class TribeCreate
    {
        // Constants for maintainability and to avoid magic strings
        private const int MaxFreeIconIndex = 11;
        private const int StandardCostAmount = 200;
        private const string CurrencyCodeCash = "CC";
        private const string CurrencyCodePremium = "NC";
        private const string KeyTribeInfo = "tribe-info";
        private const string KeyMembersPrefix = "members-0";
        
        // Parameter Keys
        private const string ParamTribe = "tribe";
        private const string ParamMember = "member";
        private const string ParamUseNaynar = "use-naynar";
        private const string ParamGroupId = "group-id";

        /// <summary>
        /// Azure Function entry point for creating a new Tribe (Group) in PlayFab.
        /// Handles validation, payments, uniqueness checks, and data initialization.
        /// </summary>
        [FunctionName("TribeCreate")]
        public static async Task<dynamic> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var syncResult = new AKMResult();

            string body = await req.ReadAsStringAsync();
            var requestData = PlayFabTools.ReadPlayFabFunctionRequest(body);
            
            if (!TryDeserializeParameters(requestData, out TribeCreationData tribeData, out TribeMember creatorData, out bool useNaynar, out string existingGroupId))
            {
                return syncResult.ReturnFailAndPrint(AKMError.NullResult, "Missing or invalid tribe/member data.", log);
            }

            if (tribeData.icon > MaxFreeIconIndex) tribeData.icon = 0;
            tribeData.creatorId = requestData.playFabId;

            // If a group ID is provided, we assume we are just updating/appending context, otherwise we create.
            if (!string.IsNullOrEmpty(existingGroupId))
            {
                // Handle logic for existing groups if necessary (omitted based on original code flow, 
                // but this is where update logic would live).
                // Returning success here as the original code fell through for existing groups without a clear path.
                return syncResult.ReturnSuccessAndPrint(log); 
            }

            // New Group Creation Flow
            tribeData.creationDate = DateTime.UtcNow;

            // Validate Inventory and Resources
            var (currencyCode, costAmount) = DetermineCost(useNaynar);
            var inventoryCheck = await ValidateFundsAsync(requestData, currencyCode, costAmount, log);
            if (!inventoryCheck.Success) return inventoryCheck.Result;

            // Check Redis Uniqueness (Tags/Names)
            var uniquenessCheck = CheckRedisUniqueness(tribeData, log);
            if (!uniquenessCheck.Success) return uniquenessCheck.Result;

            // Transaction: Deduct Cost
            // Note: We deduct BEFORE creation to prevent exploits. 
            // TODO: implement a rollback/refund mechanism if creation fails later.
            var consumeResult = await PlayFabTools.SubtractCurrency(requestData, currencyCode, costAmount);
            if (consumeResult == null)
            {
                 return syncResult.ReturnFailAndPrint(AKMError.PlayFabError, "Failed to consume currency.", log);
            }

            // Create PlayFab Group
            var groupCreation = await CreatePlayFabGroupAsync(requestData, tribeData, log);
            if (!groupCreation.Success)
            {
                // TODO: Refund currency here to ensure atomicity
                return groupCreation.Result;
            }
            string newGroupId = groupCreation.GroupId;

            // Persist Tribe Data and Redis References
            var dataPersistence = await InitializeGroupDataAsync(requestData, newGroupId, tribeData, creatorData, log);
            if (!dataPersistence.Success) return dataPersistence.Result;

            // Finalize Redis
            TribeTools.CreateRedisTribe(newGroupId, tribeData);

            return syncResult.ReturnSuccessAndPrint(log);
        }

        /// <summary>
        /// Extracts and deserializes parameters from the PlayFab request dictionary.
        /// </summary>
        private static bool TryDeserializeParameters(
            dynamic requestData, 
            out TribeCreationData tribeData, 
            out TribeMember memberData, 
            out bool useNaynar, 
            out string groupId)
        {
            tribeData = null;
            memberData = null;
            useNaynar = false;
            groupId = null;
            
            var parameters = (IDictionary<string, object>)requestData.parameters;

            if (parameters.TryGetValue(ParamTribe, out object tribeObj) && tribeObj != null)
            {
                tribeData = JsonConvert.DeserializeObject<TribeCreationData>(tribeObj.ToString());
            }

            if (parameters.TryGetValue(ParamMember, out object memberObj) && memberObj != null)
            {
                memberData = JsonConvert.DeserializeObject<TribeMember>(memberObj.ToString());
            }

            if (parameters.TryGetValue(ParamUseNaynar, out object naynarObj))
            {
                bool.TryParse(naynarObj.ToString(), out useNaynar);
            }

            if (parameters.TryGetValue(ParamGroupId, out object groupObj))
            {
                groupId = groupObj?.ToString();
            }

            return tribeData != null && memberData != null;
        }

        /// <summary>
        /// Determines the currency code and amount based on user selection.
        /// </summary>
        private static (string Code, int Amount) DetermineCost(bool useNaynar)
        {
            return useNaynar 
                ? (CurrencyCodePremium, StandardCostAmount) 
                : (CurrencyCodeCash, StandardCostAmount);
        }

        /// <summary>
        /// Verifies if the user has enough virtual currency in their inventory.
        /// </summary>
        private static async Task<(bool Success, dynamic Result)> ValidateFundsAsync(
            dynamic requestData, 
            string currencyCode, 
            int amount, 
            ILogger log)
        {
            var syncResult = new AKMResult();
            var inv = await PlayFabTools.GetInventory(requestData);

            if (inv.inventory == null || inv.playfabError != null)
            {
                return (false, syncResult.ReturnFailAndPrint(AKMError.PlayFabError, "Failed to retrieve inventory", log, inv.playfabError));
            }

            // Safe dictionary lookup using pattern matching
            if (inv.inventory.VirtualCurrency is Dictionary<string, int> vc && 
                vc.TryGetValue(currencyCode, out int currentBalance))
            {
                if (currentBalance < amount)
                {
                     return (false, syncResult.ReturnFailAndPrint(AKMError.CheatSuspected_ItemCostMismatch, $"Insufficient funds. {currencyCode}:{amount}", log));
                }
            }
            else
            {
                return (false, syncResult.ReturnFailAndPrint(AKMError.CheatSuspected_ItemCostMismatch, $"Currency {currencyCode} not found.", log));
            }

            return (true, null);
        }

        /// <summary>
        /// Checks Redis sets to ensure the Tribe Name and Tag are unique.
        /// </summary>
        /// <remarks>
        /// I'm assuming RedisDB handles its own connection pooling internally. 
        /// </remarks>
        private static (bool Success, dynamic Result) CheckRedisUniqueness(TribeCreationData tribeData, ILogger log)
        {
            var syncResult = new AKMResult();
            
            // 
            try 
            {
                if (RedisDB.SetContains(RedisDB.KEY_TRIBE_LIST, tribeData.tag))
                {
                    return (false, syncResult.ReturnFailAndPrint(AKMError.TribeTagTaken, "Tag already claimed", log));
                }

                if (RedisDB.SetContains(RedisDB.KEY_TRIBE_NAME_LIST, tribeData.name))
                {
                    return (false, syncResult.ReturnFailAndPrint(AKMError.TribeNameTaken, "Name already claimed", log));
                }
            }
            finally
            {
                // Ensure we clean up connection if the static class requires manual closing
                RedisDB.CloseDBConnection();
            }

            return (true, null);
        }

        /// <summary>
        /// Calls PlayFab Groups API to create the entity group.
        /// </summary>
        private static async Task<(bool Success, string GroupId, dynamic Result)> CreatePlayFabGroupAsync(
            dynamic requestData, 
            TribeCreationData tribeData, 
            ILogger log)
        {
            var syncResult = new AKMResult();
            
            var entity = new PlayFab.GroupsModels.EntityKey()
            {
                Id = requestData.authContext.EntityId,
                Type = requestData.authContext.EntityType
            };

            var request = new CreateGroupRequest
            {
                GroupName = tribeData.tag,
                Entity = entity,
                AuthenticationContext = requestData.authContext
            };

            var result = await PlayFabGroupsAPI.CreateGroupAsync(request);

            if (result?.Result == null || result.Error != null)
            {
                return (false, null, syncResult.ReturnFailAndPrint(AKMError.PlayFabError, "CreateGroupAsync failed.", log, result?.Error));
            }

            return (true, result.Result.Group.Id, null);
        }

        /// <summary>
        /// Initializes the tribe data objects (Info and Members) on the newly created group.
        /// </summary>
        private static async Task<(bool Success, dynamic Result)> InitializeGroupDataAsync(
            dynamic requestData, 
            string groupId, 
            TribeCreationData tribeData, 
            TribeMember creatorData, 
            ILogger log)
        {
            var syncResult = new AKMResult();
            
            var tribeEntity = new PlayFab.DataModels.EntityKey { Id = groupId, Type = "group" };

            // We do NOT need to Fetch objects first. This is a creation event, so we know the group is empty.
            // We construct the initial payload directly.
            
            var payload = new List<SetObject>
            {
                new SetObject
                {
                    ObjectName = KeyTribeInfo,
                    DataObject = JsonConvert.SerializeObject(tribeData)
                },
                new SetObject
                {
                    ObjectName = KeyMembersPrefix,
                    DataObject = creatorData.CompressedMember
                }
            };

            var updateRequest = new SetObjectsRequest
            {
                AuthenticationContext = requestData.authContext,
                Entity = tribeEntity,
                Objects = payload
            };

            var updateResult = await PlayFabDataAPI.SetObjectsAsync(updateRequest);

            if (updateResult?.Result == null || updateResult.Error != null)
            {
                return (false, syncResult.ReturnFailAndPrint(AKMError.PlayFabError, "SetObjectsAsync failed during initialization.", log, updateResult?.Error));
            }

            return (true, null);
        }
    }
}