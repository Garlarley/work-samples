namespace MySamples.Azure
{
    /// <summary>
    /// Utility class for handling Tribe (Group) logic between PlayFab and Redis.
    /// Manages data persistence, caching, and member list compression.
    /// </summary>
    public static class TribeTools
    {
        #region Constants

        private const string KeyTribeList = "tribe_list";
        private const string KeyTribeNameList = "tribe_name_list";
        private const string KeyTribePrefix = "tribe_";
        private const string KeyFactionWar = "faction_war";
        private const string KeyLeaderboardSeason = "tribe_leaderboard_season";
        private const string KeyTribeInfoObj = "tribe-info";
        private const string KeyMembersPrefix = "members-";
        private const char MemberSeparator = '|';
        private const int MaxMemberObjectBytes = 1000;

        #endregion

        #region Redis - Tribe Creation & Basic Info

        /// <summary>
        /// Initializes a new Tribe in the Redis cache.
        /// </summary>
        public static async Task<bool> CreateRedisTribe(string groupIdInPlayfab, TribeCreationData tribeData, bool closeConnection = true, int memberCount = 1)
        {
            try
            {
                await RedisDB.AddToSetAsync(KeyTribeList, tribeData.tag);
                await RedisDB.AddToSetAsync(KeyTribeNameList, tribeData.name);

                var tribe = new RedisTribe
                {
                    info = tribeData,
                    groupIdInPlayfab = groupIdInPlayfab,
                    memberCount = memberCount
                };

                await RedisDB.SetAsync(KeyTribePrefix + tribeData.tag, JsonConvert.SerializeObject(tribe));
                return true;
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Updates the member count of a specific tribe relative to its current value.
        /// </summary>
        public static async Task<bool> ModifyRedisTribeMemberCount(string tribeTag, int change, bool closeConnection = true)
        {
            try
            {
                return await UpdateRedisTribeInternal(tribeTag, tribe =>
                {
                    tribe.memberCount += change;
                    if (tribe.memberCount < 1) tribe.memberCount = 1;
                });
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Hard sets the member count of a specific tribe.
        /// </summary>
        public static async Task<bool> SetRedisTribeMemberCount(string tribeTag, int count, bool closeConnection = true)
        {
            try
            {
                return await UpdateRedisTribeInternal(tribeTag, tribe =>
                {
                    tribe.memberCount = count < 1 ? 1 : count;
                });
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Updates tribe metadata and handles name/faction change logic.
        /// </summary>
        public static async Task<bool> ModifyRedisTribeInfo(TribeCreationData tribeData, bool closeConnection = true)
        {
            try
            {
                var tribeKey = KeyTribePrefix + tribeData.tag;
                var tribeStr = await RedisDB.GetAsync(tribeKey);

                if (!tribeStr.HasValue) return true;

                var tribe = JsonConvert.DeserializeObject<RedisTribe>(tribeStr.ToString());
                if (tribe == null) return true;

                // Handle Name Change
                if (tribe.info.name != tribeData.name)
                {
                    await RedisDB.RemoveFromSetAsync(KeyTribeNameList, tribe.info.name);
                    await RedisDB.AddToSetAsync(KeyTribeNameList, tribeData.name);
                }

                // Handle Faction Change
                if (tribe.info.faction != tribeData.faction)
                {
                    tribe.factionContribution = 0;
                }

                // Ensure tag immutability
                tribeData.tag = tribe.info.tag;
                tribe.info = tribeData;

                await RedisDB.SetAsync(tribeKey, JsonConvert.SerializeObject(tribe));
                return true;
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        #endregion

        #region Redis - XP & Progression

        /// <summary>
        /// Handles Tribe XP updates, Faction War contributions, and Leaderboard ranking.
        /// </summary>
        public static async Task<bool> ModifyRedisTribeXP(string playerId, string tribeTag, int change, uint lifetimeChange, bool closeConnection = true, PlayFabTools.PlayFabRequestData requestData = null, int memberCount = 0)
        {
            if (change <= 0) return false;

            try
            {
                var tribeKey = KeyTribePrefix + tribeTag;
                var tribeStr = await RedisDB.GetAsync(tribeKey);

                if (!tribeStr.HasValue) return false;

                var tribe = JsonConvert.DeserializeObject<RedisTribe>(tribeStr.ToString());
                if (tribe == null) return false;

                // 1. Update Core Tribe Data (XP, Energy, Creator Name)
                await ProcessTribeXpAndUpdateCreator(tribe, playerId, change, lifetimeChange, memberCount, requestData);
                await RedisDB.SetAsync(tribeKey, JsonConvert.SerializeObject(tribe));

                // 2. Update Faction War
                if (tribe.info != null && tribe.info.faction >= 0)
                {
                    await UpdateFactionWar(tribe.info.faction, change);
                }

                // 3. Update Leaderboards
                if (tribe.factionContribution > 0)
                {
                    await UpdateLeaderboard(tribeTag, tribe);
                }

                return true;
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        #endregion

        #region Redis - Applicants

        /// <summary>
        /// Adds a member to the tribe's applicant list.
        /// </summary>
        public static async Task<bool> AddRedisTribeApplicant(string tribeTag, string entityId, TribeMember applicantAsMemberData, bool closeConnection = true)
        {
            if (applicantAsMemberData == null) return false;

            var applicant = new TribeApplicant
            {
                avatar = applicantAsMemberData.avatar,
                entityId = entityId,
                id = applicantAsMemberData.id,
                name = applicantAsMemberData.name,
                rank = applicantAsMemberData.rank,
                level = applicantAsMemberData.level,
                power = applicantAsMemberData.power
            };

            return await AddRedisTribeApplicant(tribeTag, applicant, closeConnection);
        }

        /// <summary>
        /// Adds a fully formed applicant object to the Redis list.
        /// </summary>
        public static async Task<bool> AddRedisTribeApplicant(string tribeTag, TribeApplicant applicant, bool closeConnection = true)
        {
            if (applicant == null) return false;

            try
            {
                return await UpdateRedisTribeInternal(tribeTag, tribe =>
                {
                    if (tribe.applicants == null) tribe.applicants = new List<TribeApplicant>();
                    
                    bool exists = tribe.applicants.Any(a => a.id == applicant.id || a.entityId == applicant.entityId);
                    if (!exists)
                    {
                        tribe.applicants.Add(applicant);
                    }
                });
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Removes a specific applicant from the Redis list.
        /// </summary>
        public static async Task<bool> RemoveRedisTribeApplicant(string tribeTag, string memberEntityId, bool closeConnection = true)
        {
            try
            {
                return await UpdateRedisTribeInternal(tribeTag, tribe =>
                {
                    if (tribe.applicants != null)
                    {
                        tribe.applicants.RemoveAll(a => a.entityId == memberEntityId);
                    }
                });
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        #endregion

        #region Redis - Retrieval

        /// <summary>
        /// Retrieves tribe JSON from Redis, optionally performing a background sync with PlayFab logic.
        /// </summary>
        public static string GetRedisTribe(string groupId, string tag, PlayFabTools.PlayFabRequestData requestData, bool closeConnection = true, bool autoKeepUpToDate = true)
        {
            try
            {
                var t = RedisDB.Get(KeyTribePrefix + tag);

                if (autoKeepUpToDate)
                {
                    // Fire and forget task to keep data fresh without blocking return
                    _ = KeepTribeUpToDate(groupId, t, tag, requestData, closeConnection);
                }
                else if (closeConnection)
                {
                    RedisDB.CloseDBConnection();
                }

                return t;
            }
            catch
            {
                if (closeConnection) RedisDB.CloseDBConnection();
                throw;
            }
        }

        /// <summary>
        /// Retrieves a raw Redis entry by key.
        /// </summary>
        public static string GetRedisEntry(string key, bool closeConnection = true)
        {
            try
            {
                return RedisDB.Get(key);
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Retrieves a randomized list of tribes, prioritizing invitations if provided.
        /// </summary>
        public static (List<string>, List<string>) GetRedisTribeList(List<string> invitations, string targetId = null, int count = 15, bool closeConnection = true)
        {
            try
            {
                var strTags = new List<string>();

                if (!string.IsNullOrEmpty(targetId))
                {
                    strTags.Add(targetId);
                }
                else
                {
                    if (invitations != null)
                    {
                        strTags.AddRange(invitations.Take(10));
                        count = Math.Max(5, count - strTags.Count);
                    }

                    var tags = RedisDB.GetSetRandom(KeyTribeList, count);
                    foreach (var tag in tags)
                    {
                        if (tag.HasValue && !strTags.Contains(tag.ToString()))
                        {
                            strTags.Add(tag.ToString());
                        }
                    }
                }

                var tribes = GetRedisTribeListInfo(strTags);
                return (strTags, tribes);
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        /// <summary>
        /// Asynchronously retrieves a randomized list of tribe details.
        /// </summary>
        public static async Task<(List<string>, List<string>)> GetRedisTribeListAsync(int count = 15, bool closeConnection = true)
        {
            try
            {
                var tags = await RedisDB.GetSetRandomAsync(KeyTribeList, count);
                var strTags = tags.Select(t => t.ToString()).ToList();
                var tribes = await GetRedisTribeListInfoAsync(tags);
                return (strTags, tribes);
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        #endregion

        #region PlayFab Data Persistence

        /// <summary>
        /// Retrieves all objects associated with a PlayFab Group Entity.
        /// </summary>
        public static async Task<List<SetObject>> GetTribeObjects(string groupId, PlayFabTools.PlayFabRequestData requestData)
        {
            var request = new GetObjectsRequest
            {
                AuthenticationContext = requestData.authContext,
                Entity = new PlayFab.DataModels.EntityKey { Id = groupId, Type = "group" }
            };

            var response = await PlayFabDataAPI.GetObjectsAsync(request);

            if (response.Error != null || response.Result == null) return null;

            return response.Result.Objects.Select(d => new SetObject
            {
                ObjectName = d.Key,
                DataObject = d.Value.DataObject
            }).ToList();
        }

        /// <summary>
        /// Persists changes to the PlayFab Group Data.
        /// </summary>
        public static async Task<bool> UpdateGroupObjects(string groupId, List<SetObject> groupData, PlayFabTools.PlayFabRequestData requestData)
        {
            var request = new SetObjectsRequest
            {
                AuthenticationContext = requestData.authContext,
                Entity = new PlayFab.DataModels.EntityKey { Id = groupId, Type = "group" },
                Objects = groupData
            };

            var result = await PlayFabDataAPI.SetObjectsAsync(request);
            return result.Error == null && result.Result != null;
        }

        /// <summary>
        /// Updates a specific member's data within the compressed PlayFab objects.
        /// </summary>
        public static async Task<bool> UpdateMemberData(string groupId, TribeMember member, PlayFabTools.PlayFabRequestData requestData)
        {
            if (member == null || string.IsNullOrEmpty(groupId)) return false;

            var groupData = await GetTribeObjects(groupId, requestData);
            if (groupData == null) return false;

            bool modified = false;

            foreach (var obj in groupData.Where(o => o.ObjectName.Contains(KeyMembersPrefix)))
            {
                var compressed = obj.DataObject.ToString();
                if (string.IsNullOrEmpty(compressed)) continue;

                if (compressed.Contains(member.id))
                {
                    var members = compressed.Split(MemberSeparator);
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (members[i].Contains(member.id))
                        {
                            members[i] = member.CompressedMember;
                            obj.DataObject = string.Join(MemberSeparator, members);
                            modified = true;
                            goto Save; 
                        }
                    }
                }
            }

            Save:
            return modified && await UpdateGroupObjects(groupId, groupData, requestData);
        }

        /// <summary>
        /// Adds a list of members to the PlayFab group data, handling object size limits.
        /// </summary>
        public static async Task<bool> AddMembersToData(string groupId, List<TribeMember> members, PlayFabTools.PlayFabRequestData requestData)
        {
            if (members == null) return false;
            members.RemoveAll(m => m == null);
            if (members.Count == 0 || string.IsNullOrEmpty(groupId)) return false;

            var groupData = await GetTribeObjects(groupId, requestData);
            if (groupData == null) return false;

            // 1. Clean existing instances of these members to prevent duplicates
            foreach (var obj in groupData.Where(o => o.ObjectName.Contains(KeyMembersPrefix)))
            {
                var compressed = obj.DataObject.ToString();
                bool objectModified = false;
                var storedMembers = compressed.Split(MemberSeparator).ToList();
                
                for (int i = storedMembers.Count - 1; i >= 0; i--)
                {
                    if (members.Any(m => storedMembers[i].Contains(m.id)))
                    {
                        storedMembers.RemoveAt(i);
                        objectModified = true;
                    }
                }

                if (objectModified)
                {
                    obj.DataObject = string.Join(MemberSeparator, storedMembers);
                }
            }

            if (members.Count == 0) return true;

            // 2. Find a slot or create a new object
            int requiredBytes = 60 * members.Count; // Approximate size
            int targetIndex = -1;
            StringBuilder sb = null;

            for (int i = 0; i < groupData.Count; i++)
            {
                if (groupData[i].ObjectName.Contains(KeyMembersPrefix))
                {
                    var currentStr = groupData[i].DataObject.ToString();
                    if (currentStr.Length <= MaxMemberObjectBytes - requiredBytes)
                    {
                        targetIndex = i;
                        sb = new StringBuilder(currentStr);
                        break;
                    }
                }
            }

            if (targetIndex == -1)
            {
                // Create new object if we aren't at the limit (assuming 5 is limit based on legacy code logic)
                if (groupData.Count >= 5) return false;
                
                sb = new StringBuilder();
                targetIndex = groupData.Count;
                groupData.Add(new SetObject { ObjectName = KeyMembersPrefix + targetIndex, DataObject = "" });
            }

            // 3. Append new members
            foreach (var mem in members)
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != MemberSeparator) sb.Append(MemberSeparator);
                sb.Append(mem.CompressedMember);
            }

            groupData[targetIndex].DataObject = sb.ToString();

            return await UpdateGroupObjects(groupId, groupData, requestData);
        }

        /// <summary>
        /// Removes members from the compressed PlayFab storage.
        /// </summary>
        public static async Task<bool> RemoveMembersFromData(string groupId, List<string> membersPlayfabId, PlayFabTools.PlayFabRequestData requestData)
        {
            if (membersPlayfabId == null) return false;
            membersPlayfabId.RemoveAll(string.IsNullOrEmpty);
            if (membersPlayfabId.Count == 0 || string.IsNullOrEmpty(groupId)) return false;

            var groupData = await GetTribeObjects(groupId, requestData);
            if (groupData == null) return false;

            bool anyModified = false;

            foreach (var obj in groupData.Where(o => o.ObjectName.Contains(KeyMembersPrefix)))
            {
                var compressed = obj.DataObject.ToString();
                if (string.IsNullOrEmpty(compressed)) continue;

                // Optimization: Check if string contains IDs before splitting
                if (!membersPlayfabId.Any(id => compressed.Contains(id))) continue;

                var splitMembers = compressed.Split(MemberSeparator).ToList();
                int removedCount = splitMembers.RemoveAll(m => membersPlayfabId.Any(id => m.Contains(id)));

                if (removedCount > 0)
                {
                    obj.DataObject = string.Join(MemberSeparator, splitMembers);
                    anyModified = true;
                }
            }

            return anyModified && await UpdateGroupObjects(groupId, groupData, requestData);
        }

        #endregion

        #region Player Data Helper

        /// <summary>
        /// Adds a tribe application tag to the player's public data.
        /// </summary>
        public static async Task<bool> AddToPlayerAppliedTribeList(string tribeTag, string playerId, PlayFabTools.PlayFabRequestData requestData)
        {
            var tribes = await GetPlayerTribeApplications(playerId, requestData);
            
            if (!tribes.tribes.Contains(tribeTag))
            {
                tribes.tribes.Add(tribeTag);
                return await SavePlayerTribeApplications(playerId, tribes, requestData);
            }
            return true;
        }

        /// <summary>
        /// Clears tribe applications for a player, optionally preserving one exception.
        /// </summary>
        public static async Task<bool> ClearPlayerAppliedTribeList(string exceptionTag, string playerId, string playerEntityId, PlayFabTools.PlayFabRequestData requestData, bool closeConnection = false)
        {
            try
            {
                var apps = await GetPlayerTribeApplications(playerId, requestData);

                if (apps.tribes != null && apps.tribes.Count > 0)
                {
                    foreach (var tag in apps.tribes)
                    {
                        if (string.IsNullOrEmpty(exceptionTag) || tag != exceptionTag)
                        {
                            // Note: Calling false here to keep connection open for the loop
                            await RemoveRedisTribeApplicant(tag, playerEntityId, false);
                        }
                    }

                    apps.tribes.Clear();
                    await SavePlayerTribeApplications(playerId, apps, requestData);
                }
                return true;
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        #endregion

        #region Private Helper Methods

        private static async Task<bool> UpdateRedisTribeInternal(string tribeTag, Action<RedisTribe> modifyAction)
        {
            var key = KeyTribePrefix + tribeTag;
            var tribeStr = await RedisDB.GetAsync(key);
            
            if (!tribeStr.HasValue) return false;

            var tribe = JsonConvert.DeserializeObject<RedisTribe>(tribeStr.ToString());
            if (tribe == null) return false;

            modifyAction(tribe);

            await RedisDB.SetAsync(key, JsonConvert.SerializeObject(tribe));
            return true;
        }

        private static async Task ProcessTribeXpAndUpdateCreator(RedisTribe tribe, string playerId, int change, uint lifetimeChange, int memberCount, PlayFabTools.PlayFabRequestData requestData)
        {
            if (memberCount > 0) tribe.memberCount = memberCount;

            // Fetch Creator Name if necessary
            if (playerId == tribe.creatorId && requestData != null)
            {
                var profileReq = new GetPlayerProfileRequest
                {
                    AuthenticationContext = requestData.authContext,
                    PlayFabId = requestData.playFabId
                };
                var profileRes = await PlayFabServerAPI.GetPlayerProfileAsync(profileReq);
                if (profileRes?.Result?.PlayerProfile != null)
                {
                    tribe.creatorName = profileRes.Result.PlayerProfile.DisplayName;
                }
            }

            // Energy & XP Logic
            if (tribe.Level < 20)
            {
                if (change > tribe.energy)
                {
                    tribe.xp += tribe.energy;
                    tribe.energy = 0;
                }
                else
                {
                    tribe.xp += change;
                    tribe.energy -= change;
                }
            }

            // Contribution Logic
            tribe.factionContribution += change;
            if (tribe.contributions == null) tribe.contributions = new Dictionary<string, uint>();
            
            if (!tribe.contributions.ContainsKey(playerId)) tribe.contributions.Add(playerId, 0);

            // Recovery logic for lost cache vs incremental update
            if (tribe.contributions[playerId] == 0 && lifetimeChange > change)
            {
                tribe.contributions[playerId] = lifetimeChange;
            }
            else
            {
                tribe.contributions[playerId] += (uint)change;
            }
        }

        private static async Task UpdateFactionWar(int factionId, int change)
        {
            var factionStr = await RedisDB.GetAsync(KeyFactionWar);
            var fwar = factionStr.HasValue 
                ? JsonConvert.DeserializeObject<RedisFactionWar>(factionStr.ToString()) 
                : new RedisFactionWar();

            fwar.ResetWarIfNeedBe();
            fwar.AddScore(change, factionId);
            await RedisDB.SetAsync(KeyFactionWar, JsonConvert.SerializeObject(fwar));
        }

        private static async Task UpdateLeaderboard(string tribeTag, RedisTribe tribe)
        {
            var lstr = await RedisDB.GetAsync(KeyLeaderboardSeason);
            var lb = lstr.HasValue 
                ? JsonConvert.DeserializeObject<RedisTribeLeaderboard>(lstr.ToString()) 
                : new RedisTribeLeaderboard();

            if (lb.entries == null) lb.entries = new List<RedisLeaderboardEntry>();

            bool shouldInsert = lb.entries.Count < 50 || lb.entries.Any(e => e.score < tribe.factionContribution);
            var existingEntry = lb.entries.FirstOrDefault(e => e.tribeTag == tribeTag);

            if (shouldInsert)
            {
                if (existingEntry == null)
                {
                    if (lb.entries.Count >= 50) lb.entries.RemoveAt(lb.entries.Count - 1);
                    
                    lb.entries.Add(new RedisLeaderboardEntry
                    {
                        tribeTag = tribeTag,
                        tribeName = tribe.info?.name ?? string.Empty,
                        score = tribe.factionContribution,
                        icon = tribe.info?.icon ?? 0,
                        faction = tribe.info?.faction ?? -1,
                        level = tribe.Level,
                        members = tribe.memberCount,
                        leaderName = tribe.creatorName
                    });
                }
                else
                {
                    existingEntry.score = tribe.factionContribution;
                    existingEntry.tribeName = tribe.info?.name;
                    existingEntry.faction = tribe.info?.faction ?? -1;
                    existingEntry.icon = tribe.info?.icon ?? 0;
                }

                lb.entries.Sort((x, y) => y.score.CompareTo(x.score));
                await RedisDB.SetAsync(KeyLeaderboardSeason, JsonConvert.SerializeObject(lb));
            }
        }

        private static List<string> GetRedisTribeListInfo(List<string> tags)
        {
            var tribes = new List<string>();
            foreach (var tag in tags)
            {
                var tribe = RedisDB.Get(KeyTribePrefix + tag);
                if (!string.IsNullOrEmpty(tribe)) tribes.Add(tribe);
            }
            return tribes;
        }

        private static async Task<List<string>> GetRedisTribeListInfoAsync(RedisValue[] tags)
        {
            var tribes = new List<string>();
            foreach (var tag in tags)
            {
                if (tag.HasValue)
                {
                    var tribe = await RedisDB.GetAsync(KeyTribePrefix + tag.ToString());
                    if (!string.IsNullOrEmpty(tribe)) tribes.Add(tribe);
                }
            }
            return tribes;
        }

        private static async Task<bool> KeepTribeUpToDate(string groupId, RedisValue tribeStr, string tribeTag, PlayFabTools.PlayFabRequestData requestData, bool closeConnection)
        {
            try
            {
                if (!tribeStr.HasValue) return false;

                var tribe = JsonConvert.DeserializeObject<RedisTribe>(tribeStr.ToString());
                if (tribe == null) return false;

                bool changed = tribe.GrantEnergy();
                if (await BackupRedisTribe(groupId, tribe, requestData)) changed = true;

                if (changed)
                {
                    await RedisDB.SetAsync(KeyTribePrefix + tribeTag, JsonConvert.SerializeObject(tribe));
                }
                return true;
            }
            finally
            {
                if (closeConnection) RedisDB.CloseDBConnection();
            }
        }

        private static async Task<PlayerTribeApplications> GetPlayerTribeApplications(string playerId, PlayFabTools.PlayFabRequestData requestData)
        {
            var request = new GetUserDataRequest
            {
                AuthenticationContext = requestData.authContext,
                PlayFabId = playerId,
                Keys = new List<string> { PlayFabTools.PLAYERDATA_PUBLIC_TRIBE_APPLICATIONS }
            };

            var data = await PlayFabServerAPI.GetUserDataAsync(request);
            if (data.Error != null || data.Result?.Data == null) return new PlayerTribeApplications();

            if (data.Result.Data.TryGetValue(PlayFabTools.PLAYERDATA_PUBLIC_TRIBE_APPLICATIONS, out var record) && record != null)
            {
                var apps = JsonConvert.DeserializeObject<PlayerTribeApplications>(record.Value);
                if (apps.tribes == null) apps.tribes = new List<string>();
                return apps;
            }

            return new PlayerTribeApplications { tribes = new List<string>() };
        }

        private static async Task<bool> SavePlayerTribeApplications(string playerId, PlayerTribeApplications apps, PlayFabTools.PlayFabRequestData requestData)
        {
            var request = new UpdateUserDataRequest
            {
                AuthenticationContext = requestData.authContext,
                PlayFabId = playerId,
                Data = new Dictionary<string, string>
                {
                    { PlayFabTools.PLAYERDATA_PUBLIC_TRIBE_APPLICATIONS, JsonConvert.SerializeObject(apps) }
                }
            };
            var result = await PlayFabServerAPI.UpdateUserDataAsync(request);
            return result.Error == null;
        }

        #endregion
    }
}