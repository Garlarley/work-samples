using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace MySamples.Assets
{
    public class AssetPreloader : MonoBehaviour
    {
        #region Configuration

        [Tooltip("If true, ignores tick limits and loads all assets immediately.")]
        public bool rushPreloaderBeforeSimulation;

        public MDQDatabase[] databases;

        [InfoBox("If > 0, limits the number of Addressable calls per batch to manage frame rate.")]
        public int assetCallPerTick = 0;

        [Tooltip("Override for assetCallPerTick when a loading screen is detected.")]
        public int assetCallPerTickIfLoadingScreen = 0;

        [InfoBox("Determines cleanup behavior upon completion.")]
        public bool destroyGameObjectWhenDone = false;

        #endregion

        #region State

        private const int RUSH_BATCH_SIZE = 50;

        /// <summary>
        /// Tracks global preloading operations to prevent overlapping simulations.
        /// </summary>
        private static int _activePreloaderCount = 0;

        internal static readonly HashSet<string> LoadedAssetKeys = new HashSet<string>();

        public static bool IsAnyPreloaderRunning => _activePreloaderCount > 0;
        public bool IsLoading { get; private set; }

        private bool _isDestroyed;

        #endregion

        private void Start()
        {
            // Fire and forget, but handled safely
            _ = RunPreloadSequence();
        }

        private void OnDestroy()
        {
            _isDestroyed = true;
        }

        /// <summary>
        /// Determines the batch size for the current frame based on game state.
        /// </summary>
        protected int CurrentBatchSize
        {
            get
            {
                if (rushPreloaderBeforeSimulation) return RUSH_BATCH_SIZE;
                
                if (assetCallPerTickIfLoadingScreen > 0 && MD.UI.LoadingUI.IsScreenVisible()) 
                {
                    return assetCallPerTickIfLoadingScreen;
                }
                
                return assetCallPerTick;
            }
        }

        /// <summary>
        /// Orchestrates the asynchronous loading of all configured databases.
        /// </summary>
        private async Task RunPreloadSequence()
        {
            _activePreloaderCount++;
            IsLoading = true;

            try
            {
                var loadingTasks = new List<Task>();

                foreach (var database in databases)
                {
                    if (_isDestroyed) break;
                    if (database == null || database.db == null) continue;

                    await ProcessDatabaseAsync(database, loadingTasks);
                }
            }
            finally
            {
                _activePreloaderCount--;
                IsLoading = false;
                Cleanup();
            }
        }

        /// <summary>
        /// Iterates through a specific database and schedules asset loads, respecting batch limits.
        /// </summary>
        private async Task ProcessDatabaseAsync(MDQDatabase database, List<Task> taskBuffer)
        {
            int batchSize = CurrentBatchSize;
            bool useThrottling = batchSize > 0;

            for (int i = 0; i < database.db.Length; i++)
            {
                if (_isDestroyed) return;

                string assetKey = database.db[i];
                if (ShouldSkipAsset(assetKey)) continue;

                // Mark as loaded immediately to prevent duplicates in other preloaders
                LoadedAssetKeys.Add(assetKey);
                
                // Queue the load operation
                taskBuffer.Add(LoadAndTrackAsset(assetKey));

                // Handle Throttling
                if (useThrottling && taskBuffer.Count >= batchSize)
                {
                    await Task.WhenAll(taskBuffer);
                    taskBuffer.Clear();

                    batchSize = CurrentBatchSize; 
                }
            }

            // Await remaining tasks in the buffer
            if (taskBuffer.Count > 0)
            {
                await Task.WhenAll(taskBuffer);
                taskBuffer.Clear();
            }
        }

        /// <summary>
        /// Initiates the Addressable load operation.
        /// </summary>
        private async Task LoadAndTrackAsset(string key)
        {
            await Addressables.LoadAssetAsync<AssetBase>(key).Task;
        }

        /// <summary>
        /// Validates if an asset key is valid and hasn't been loaded yet.
        /// </summary>
        private bool ShouldSkipAsset(string key)
        {
            return string.IsNullOrEmpty(key) || LoadedAssetKeys.Contains(key);
        }

        /// <summary>
        /// Handles the destruction of the component or game object upon completion.
        /// </summary>
        private void Cleanup()
        {
            if (_isDestroyed) return;

            if (destroyGameObjectWhenDone)
            {
                Destroy(gameObject);
            }
            else
            {
                Destroy(this);
            }
        }
    }
}