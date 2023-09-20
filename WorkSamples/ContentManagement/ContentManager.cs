using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace MySamples.Assets
{
    public static class ExtendedAddressableFunctions
    {
        /// <summary>
        /// Safely loads an AssetReference, invoking the callback immediately if already loaded, or upon completion.
        /// </summary>
        public static void SafeAddressableLoad<T>(this AssetReference assetRef, System.Action<T> onLoaded) where T : UnityEngine.Object
        {
            if (!assetRef.RuntimeKeyIsValid() || onLoaded == null) return;

            // Scenario 1: Asset is already loaded and valid
            if (assetRef.IsValid() && assetRef.OperationHandle.IsValid() && assetRef.IsDone)
            {
                if (assetRef.OperationHandle.Result is T result)
                {
                    onLoaded.Invoke(result);
                    return;
                }
            }

            // Scenario 2: Asset is currently loading or needs to be loaded
            if (assetRef.IsValid() && !assetRef.IsDone)
            {
                assetRef.OperationHandle.Completed += handle =>
                {
                    if (handle.IsValid() && handle.Result is T result)
                    {
                        onLoaded.Invoke(result);
                    }
                };
            }
            // Scenario 3: Asset reference hasn't been instantiated yet
            else
            {
                assetRef.LoadAssetAsync<T>().Completed += handle =>
                {
                    if (handle.IsValid() && handle.Result != null)
                    {
                        onLoaded.Invoke(handle.Result);
                    }
                };
            }
        }
    }

    namespace MD.ContentManagement
    {
        public static class ContentManager
        {
            public static Events.UnityAction OnNextContentUpdateComplete;
            public static Events.UnityAction OnAnyContentUpdateComplete;

            // Cache reflection info to avoid performance hits on repeated calls
            private static FieldInfo _operationCacheField;

            /// <summary>
            /// Initiates a check for Addressable catalog updates.
            /// </summary>
            public static void CheckForContentUpdate()
            {
                Addressables.CheckForCatalogUpdates().Completed += HandleCheckForCatalogUpdatesCompleted;
            }

            /// <summary>
            /// Handles the result of the catalog update check.
            /// </summary>
            private static void HandleCheckForCatalogUpdatesCompleted(AsyncOperationHandle<List<string>> handle)
            {
                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null && handle.Result.Count > 0)
                {
                    Addressables.UpdateCatalogs(handle.Result).Completed += HandleUpdateCatalogsCompleted;
                }
                else
                {
                    InvokeUpdateCallbacks();
                }
            }

            /// <summary>
            /// Handles the completion of the catalog update process.
            /// </summary>
            private static void HandleUpdateCatalogsCompleted(AsyncOperationHandle<List<IResourceLocator>> handle)
            {
                InvokeUpdateCallbacks();
            }

            private static void InvokeUpdateCallbacks()
            {
                if (OnNextContentUpdateComplete != null)
                {
                    OnNextContentUpdateComplete.Invoke();
                    OnNextContentUpdateComplete = null;
                }

                OnAnyContentUpdateComplete?.Invoke();
            }

            /// <summary>
            /// Forcefully releases all active AsyncOperationHandles in the Addressables Resource Manager.
            /// </summary>
            /// <remarks>
            /// This uses Reflection to access internal Addressables cache. 
            /// Use with caution as internal APIs may change between Unity versions.
            /// </remarks>
            public static void UnloadAllAddressableHandles(bool unloadMDQMaps = true)
            {
                var handles = GetAllAsyncOperationHandles(unloadMDQMaps);
                ReleaseAsyncOperationHandles(handles);
            }

            /// <summary>
            /// Retrieves all active handles via reflection.
            /// </summary>
            public static List<AsyncOperationHandle> GetAllAsyncOperationHandles(bool unloadMDQMaps = true)
            {
                var handles = new List<AsyncOperationHandle>();

                // Lazy load reflection field info
                if (_operationCacheField == null)
                {
                    var resourceManagerType = Addressables.ResourceManager.GetType();
                    _operationCacheField = resourceManagerType.GetField("m_AssetOperationCache", 
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (_operationCacheField == null)
                {
                    Debug.LogError("[ContentManager] Failed to acquire internal AssetOperationCache field via reflection.");
                    return handles;
                }

                var dictionary = _operationCacheField.GetValue(Addressables.ResourceManager) as IDictionary;
                if (dictionary == null) return handles;

                foreach (var asyncOperationInterface in dictionary.Values)
                {
                    if (asyncOperationInterface == null) continue;

                    var handle = (AsyncOperationHandle)typeof(AsyncOperationHandle).InvokeMember(
                        nameof(AsyncOperationHandle),
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.CreateInstance,
                        null, null, new object[] { asyncOperationInterface });

                    handles.Add(handle);
                }

                return handles;
            }

            /// <summary>
            /// Releases a specific list of handles.
            /// </summary>
            public static void ReleaseAsyncOperationHandles(List<AsyncOperationHandle> handles)
            {
                foreach (var handle in handles)
                {
                    if (handle.IsValid())
                    {
                        Addressables.Release(handle);
                    }
                }
            }
        }
    }
}