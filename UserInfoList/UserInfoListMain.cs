using FrooxEngine;
using FrooxEngine.CommonAvatar;
using Elements.Core;
using Elements.Assets;
using SkyFrost.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

#nullable enable
namespace UserInfoList
{
    /// <summary>
    /// Static manager that handles comprehensive user information display in a hierarchical slot structure.
    /// Provides real-time updates of user data including FPS, network stats, presence, profile, and account information.
    /// </summary>
    public static class UserListManager
    {
        private static readonly Dictionary<UserRoot, UserListInstance> _instances = new();
        
        public static void Initialize(UserRoot userRoot)
        {
            if (!_instances.ContainsKey(userRoot))
            {
                _instances[userRoot] = new UserListInstance(userRoot);
                UniLog.Log($"UserListManager initialized for UserRoot: {userRoot.ActiveUser?.UserName ?? "Unknown"}");
            }
        }
        
        public static void Cleanup(UserRoot userRoot)
        {
            if (_instances.TryGetValue(userRoot, out var instance))
            {
                instance.Dispose();
                _instances.Remove(userRoot);
                UniLog.Log($"UserListManager cleaned up for UserRoot: {userRoot.ActiveUser?.UserName ?? "Unknown"}");
            }
        }
        
        public static void ReloadAll()
        {
            UniLog.Log($"Reloading all UserList instances ({_instances.Count} total)");
            foreach (var instance in _instances.Values)
            {
                instance.ManualReload();
            }
        }
        
        public static void ReloadForUser(UserRoot userRoot)
        {
            if (_instances.TryGetValue(userRoot, out var instance))
            {
                instance.ManualReload();
            }
            else
            {
                UniLog.Warning($"No UserList instance found for UserRoot: {userRoot.ActiveUser?.UserName ?? "Unknown"}");
                // Try to reinitialize
                Initialize(userRoot);
            }
        }
    }

    /// <summary>
    /// Instance that manages user information for a specific UserRoot with event-based updates.
    /// </summary>
    public class UserListInstance
    {
        private readonly UserRoot _userRoot;
        private readonly Dictionary<string, UserInfoSlot> _userSlots = new();
        private readonly Dictionary<string, DateTime> _lastUpdates = new();
        private int _userCounter = 1;
        private DynamicValueVariable<int>? _userCountVariable; // Variable for total user count
        private CancellationTokenSource? _cancellationTokenSource;
        private DateTime _lastSlotNameUpdate = DateTime.MinValue;
        
        private const float UPDATE_INTERVAL = 1.0f;
        private const float SLOT_NAME_UPDATE_INTERVAL = 5.0f; // Update names every 5 seconds
        private const string USER_LIST_SLOT_NAME = "UserList";
        private const string DYNAMIC_VARIABLE_PREFIX = "World/Userlist";

        public UserListInstance(UserRoot userRoot)
        {
            _userRoot = userRoot;
            _cancellationTokenSource = new CancellationTokenSource();
            
            InitializeUserList();
            
            // Subscribe to world events for immediate user join/leave notifications
            _userRoot.World.UserJoined += HandleUserJoined;
            _userRoot.World.UserLeft += HandleUserLeft;
            
            // Initialize with existing users in the world
            InitializeExistingUsers();
            
            // Start reduced update loop for data updates only
            _userRoot.StartTask(() => UpdateLoop(_cancellationTokenSource.Token));
        }

        private async Task UpdateLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && 
                   _userRoot != null && !_userRoot.IsRemoved)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(UPDATE_INTERVAL), cancellationToken);
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        // Only update existing user data, not add/remove (handled by events)
                        SafeExecute(UpdateExistingUsers, "UpdateExistingUsers");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    UniLog.Warning($"Error in UserList update loop: {ex.Message}");
                }
            }
        }

        private void InitializeUserList()
        {
            var userListSlot = _userRoot.Slot.AddSlot(USER_LIST_SLOT_NAME);
            userListSlot.PersistentSelf = false;
            
            // Create dynamic variable for user count
            _userCountVariable = userListSlot.AttachComponent<DynamicValueVariable<int>>();
            _userCountVariable.VariableName.Value = "World/Userlist-UserCount";
            _userCountVariable.Value.Value = 0;
        }

        private void InitializeExistingUsers()
        {
            // Add all users that are already in the world when we start
            var existingUsers = _userRoot.World.AllUsers.Where(u => u != null && !u.IsRemoved).ToList();
            
            UniLog.Log($"UserList initializing with {existingUsers.Count} existing users");
            
            foreach (var user in existingUsers)
            {
                SafeExecute(() => HandleUserJoined(user), $"InitializeExistingUser-{user.UserName}");
            }
        }

        // Event-based user management - immediate response to user join/leave

        private void HandleUserJoined(FrooxEngine.User user)
        {
            try
            {
                var userListSlot = _userRoot.Slot.FindChild(USER_LIST_SLOT_NAME);
                if (userListSlot == null) 
                {
                    UniLog.Warning("UserList slot not found when trying to add user");
                    return;
                }

                var userId = user.UserID;
                if (string.IsNullOrEmpty(userId))
                {
                    UniLog.Warning($"User {user.UserName} has empty UserID");
                    return;
                }
                
                if (_userSlots.ContainsKey(userId))
                {
                    UniLog.Log($"User {user.UserName} ({userId}) already exists in UserList");
                    return;
                }

                var userSlotName = $"User{_userCounter}";
                _userCounter++;
                
                UniLog.Log($"Creating UserList slot for: {user.UserName} ({userId}) as {userSlotName}");
                
                var userInfoSlot = new UserInfoSlot(userListSlot, user, userId, userSlotName);
                _userSlots[userId] = userInfoSlot;
                _lastUpdates[userId] = DateTime.UtcNow;
                
                UpdateUserCount();
                UniLog.Log($"User successfully added to UserList: {user.UserName} ({userId}) - Total users: {_userSlots.Count}");
            }
            catch (Exception ex)
            {
                UniLog.Error($"Error adding user {user.UserName} to UserList: {ex}");
            }
        }

        private void HandleUserLeft(FrooxEngine.User user)
        {
            var userId = user.UserID;
            if (string.IsNullOrEmpty(userId) || !_userSlots.TryGetValue(userId, out var userInfoSlot)) return;

            userInfoSlot.CleanupSlot();
            _userSlots.Remove(userId);
            _lastUpdates.Remove(userId);
            
            UpdateUserCount();
            UniLog.Log($"User left UserList: {user.UserName} ({userId})");
        }

        private void UpdateExistingUsers()
        {
            var now = DateTime.UtcNow;
            bool updateSlotNames = (now - _lastSlotNameUpdate).TotalSeconds > SLOT_NAME_UPDATE_INTERVAL;

            // Check if UserList slot still exists and is properly configured
            var userListSlot = _userRoot.Slot.FindChild(USER_LIST_SLOT_NAME);
            if (userListSlot == null || _userCountVariable == null || _userCountVariable.IsRemoved)
            {
                UniLog.Warning("UserList slot or components were manually deleted - recreating and reloading all users");
                ReloadUserList();
                return;
            }

            // Get current users in world
            var currentUsers = _userRoot.World.AllUsers.Where(u => u != null && !u.IsRemoved).ToList();
            
            // Check for users in world that we don't have slots for (backup mechanism)
            foreach (var user in currentUsers)
            {
                var userId = user.UserID;
                if (!string.IsNullOrEmpty(userId) && !_userSlots.ContainsKey(userId))
                {
                    UniLog.Warning($"Found user {user.UserName} not in UserList - adding via backup mechanism");
                    SafeExecute(() => HandleUserJoined(user), $"BackupAdd-{user.UserName}");
                }
            }

            // Update existing user slots
            foreach (var kvp in _userSlots.ToList()) // ToList to avoid modification during iteration
            {
                var userId = kvp.Key;
                var userInfoSlot = kvp.Value;
                
                // Find the user in the world
                var user = currentUsers.FirstOrDefault(u => u.UserID == userId);
                if (user != null && !user.IsRemoved)
                {
                    // Check if we need to update this user (throttle updates)
                    if (_lastUpdates.TryGetValue(userId, out var lastUpdate))
                    {
                        if ((now - lastUpdate).TotalSeconds < UPDATE_INTERVAL)
                            continue;
                    }

                    userInfoSlot.UpdateUserInfo(user, updateSlotNames);
                    _lastUpdates[userId] = now;
                }
                else
                {
                    // User is no longer in world but we still have their slot - clean up
                    UniLog.Log($"User {userId} no longer in world - removing from UserList");
                    userInfoSlot.CleanupSlot();
                    _userSlots.Remove(userId);
                    _lastUpdates.Remove(userId);
                    UpdateUserCount();
                }
            }

            if (updateSlotNames)
                _lastSlotNameUpdate = now;
        }

        private void UpdateUserCount()
        {
            if (_userCountVariable != null)
            {
                _userCountVariable.Value.Value = _userSlots.Count;
            }
        }

        private void ReloadUserList()
        {
            try
            {
                UniLog.Log("Reloading UserList - clearing existing data and recreating");
                
                // Clear existing user slots (they may be invalid now)
                foreach (var userSlot in _userSlots.Values)
                {
                    try
                    {
                        userSlot.CleanupSlot();
                    }
                    catch (Exception ex)
                    {
                        UniLog.Warning($"Error cleaning up slot during reload: {ex.Message}");
                    }
                }
                _userSlots.Clear();
                _lastUpdates.Clear();
                
                // Reset user counter
                _userCounter = 1;
                
                // Recreate the UserList slot structure
                InitializeUserList();
                
                // Re-add all existing users
                InitializeExistingUsers();
                
                UniLog.Log($"UserList reload completed - {_userSlots.Count} users restored");
            }
            catch (Exception ex)
            {
                UniLog.Error($"Error during UserList reload: {ex}");
            }
        }

        public void ManualReload()
        {
            UniLog.Log("Manual UserList reload requested");
            SafeExecute(ReloadUserList, "ManualReload");
        }

        private void SafeExecute(Action action, string context)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                UniLog.Error($"Exception in {context}: {ex}");
            }
        }

        public void Dispose()
        {
            // Cancel update loop
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            
            // Unsubscribe from world events
            if (_userRoot?.World != null)
            {
                _userRoot.World.UserJoined -= HandleUserJoined;
                _userRoot.World.UserLeft -= HandleUserLeft;
            }
            
            // Clean up all user slots
            foreach (var userSlot in _userSlots.Values)
            {
                userSlot.CleanupSlot();
            }
            _userSlots.Clear();
            _lastUpdates.Clear();
        }
    }

    /// <summary>
    /// Represents a comprehensive information display for a single user, organized in hierarchical categories.
    /// Manages dynamic variables for real-time user data updates and cloud-based account information.
    /// </summary>
    public class UserInfoSlot
    {
        private const string CLOUD_DATA_FETCH_ERROR_MESSAGE = "Error";
        private const string UNKNOWN_VALUE = "Unknown";
        private const string FAILED_TO_LOAD = "Failed to load";
        private const int DAYS_PER_YEAR = 365;
        private readonly Slot _userSlot;
        private readonly Slot _performanceSlot;
        private readonly Slot _networkSlot;
        private readonly Slot _presenceSlot;
        private readonly Slot _sessionSlot;
        private readonly Slot _profileSlot;
        private readonly Slot _trackingSlot;
        private readonly Slot _privacySlot;
        private readonly Slot _accountSlot;
        private readonly string _userId;
        private readonly int _userIndex; // Simple numeric index for variable names
        private readonly DynamicReferenceVariable<FrooxEngine.User> _userReference;
        private readonly DynamicValueVariable<string> _userName;
        private DynamicValueVariable<float> _fpsValue = null!;
        private DynamicValueVariable<string> _fpsString = null!;
        private DynamicValueVariable<int> _queuedPackets = null!;
        private DynamicValueVariable<string> _badges = null!;
        private readonly List<DynamicValueVariable<Uri>> _badgeUrls = new(); // For individual badge URLs
        private DynamicValueVariable<int> _badgeCount = null!; // For badge count
        private DynamicReferenceVariable<AudioStream<MonoSample>> _voiceStream = null!;
        private bool _voiceStreamFound = false; // Track if we've found the voice stream
        private bool _lastCloudDataLoaded = false; // Track cloud data load state
        private FrooxEngine.User? _lastUser = null; // Track last user for slot name updates
        
        // Network information
        private DynamicValueVariable<int> _ping = null!;
        private DynamicValueVariable<float> _packetLoss = null!;
        private DynamicValueVariable<float> _downloadSpeed = null!;
        private DynamicValueVariable<float> _uploadSpeed = null!;
        
        // Presence information
        private DynamicValueVariable<bool> _presentInWorld = null!;
        private DynamicValueVariable<bool> _presentInHeadset = null!;
        private DynamicValueVariable<bool> _vrActive = null!;
        private DynamicValueVariable<bool> _isMuted = null!;
        private DynamicValueVariable<string> _voiceMode = null!;
        
        // Session information
        private DynamicValueVariable<string> _platform = null!;
        private DynamicValueVariable<bool> _isHost = null!;
        private DynamicValueVariable<bool> _editMode = null!;
        
        // Profile information
        private DynamicValueVariable<string> _headDevice = null!;
        private DynamicValueVariable<string> _primaryHand = null!;
        
        // Advanced tracking
        private DynamicValueVariable<bool> _eyeTracking = null!;
        private DynamicValueVariable<bool> _pupilTracking = null!;
        private DynamicValueVariable<string> _mouthTracking = null!;
        
        // Privacy & Settings
        private DynamicValueVariable<bool> _hideInScreenshots = null!;
        private DynamicValueVariable<bool> _mediaMetadataOptOut = null!;
        private DynamicValueVariable<string> _utcOffset = null!;
        
        // Account Information (using CloudUserInfo)
        private CloudUserInfo _cloudUserInfo = null!;
        private DynamicValueVariable<string> _registrationDate = null!;
        private DynamicValueVariable<int> _accountAgeInDays = null!;
        private DynamicValueVariable<bool> _isAccountBirthday = null!;
        private DynamicValueVariable<string> _profileIconUrl = null!;

        // Cached values for optimization - only update dynamic variables when values change
        private float _lastFps = float.MinValue;
        private int _lastQueuedPackets = int.MinValue;
        private string _lastBadges = "";
        private int _lastPing = int.MinValue;
        private float _lastPacketLoss = float.MinValue;
        private bool _lastPresentInWorld = false;
        private bool _lastPresentInHeadset = false;
        private bool _lastVrActive = false;
        private bool _lastIsMuted = false;
        private string _lastPlatform = "";
        private bool _lastIsHost = false;
        private bool _lastEditMode = false;
        

        public UserInfoSlot(Slot parentSlot, FrooxEngine.User user, string userId, string userSlotName)
        {
            _userId = userId;
            
            
            // Extract numeric index from userSlotName (e.g., "User1" -> 1)
            if (userSlotName.StartsWith("User") && int.TryParse(userSlotName.Substring(4), out int index))
            {
                _userIndex = index;
            }
            else
            {
                _userIndex = 1; // fallback
            }
            
            // Create user slot structure with numbered name but actual username as slot name
            _userSlot = parentSlot.AddSlot(userSlotName);
            _userSlot.Name = user.UserName ?? UNKNOWN_VALUE; 
            _userSlot.PersistentSelf = false;

            _performanceSlot = _userSlot.AddSlot("Performance");
            _performanceSlot.PersistentSelf = false;
            
            _networkSlot = _userSlot.AddSlot("Network");
            _networkSlot.PersistentSelf = false;
            
            _presenceSlot = _userSlot.AddSlot("Presence");
            _presenceSlot.PersistentSelf = false;
            
            _sessionSlot = _userSlot.AddSlot("Session");
            _sessionSlot.PersistentSelf = false;
            
            _profileSlot = _userSlot.AddSlot("Profile");
            _profileSlot.PersistentSelf = false;
            
            _trackingSlot = _userSlot.AddSlot("Tracking");
            _trackingSlot.PersistentSelf = false;
            
            _privacySlot = _userSlot.AddSlot("Privacy");
            _privacySlot.PersistentSelf = false;
            
            _accountSlot = _userSlot.AddSlot("Account");
            _accountSlot.PersistentSelf = false;
            
            // Create child slots for each category
            CreatePerformanceChildSlots();
            CreateNetworkChildSlots();
            CreatePresenceChildSlots();
            CreateSessionChildSlots();
            CreateProfileChildSlots();
            CreateTrackingChildSlots();
            CreatePrivacyChildSlots();
            CreateAccountChildSlots();

            // Create dynamic variables for user reference and name
            _userReference = _userSlot.AttachComponent<DynamicReferenceVariable<FrooxEngine.User>>();
            _userReference.VariableName.Value = $"World/Userlist-{_userIndex}";
            _userReference.Reference.Target = user;

            _userName = _userSlot.AttachComponent<DynamicValueVariable<string>>();
            _userName.VariableName.Value = $"World/Userlist-{_userIndex}-name";
            _userName.Value.Value = user.UserName ?? UNKNOWN_VALUE;

            // Set initial formatted slot names
            UpdateSlotNames(user);
        }
        
        private void CreatePerformanceChildSlots()
        {
            var userIndex = _userIndex;
            
            // Create child slots for performance information
            var fpsSlot = _performanceSlot.AddSlot("FPS");
            fpsSlot.PersistentSelf = false;
            _fpsValue = fpsSlot.AttachComponent<DynamicValueVariable<float>>();
            _fpsValue.VariableName.Value = $"World/Userlist-{userIndex}-fps";
            
            _fpsString = fpsSlot.AttachComponent<DynamicValueVariable<string>>();
            _fpsString.VariableName.Value = $"World/Userlist-{userIndex}-fps-string";
        }
        
        private void CreateNetworkChildSlots()
        {
            var userIndex = _userIndex; // Use user ID for uniqueness
            
            // Create child slots for network information
            var pingSlot = _networkSlot.AddSlot("Ping");
            pingSlot.PersistentSelf = false;
            _ping = pingSlot.AttachComponent<DynamicValueVariable<int>>();
            _ping.VariableName.Value = $"World/Userlist-{userIndex}-ping";
            
            var packetLossSlot = _networkSlot.AddSlot("PacketLoss");
            packetLossSlot.PersistentSelf = false;
            _packetLoss = packetLossSlot.AttachComponent<DynamicValueVariable<float>>();
            _packetLoss.VariableName.Value = $"World/Userlist-{userIndex}-packetloss";
            
            var downloadSpeedSlot = _networkSlot.AddSlot("DownloadSpeed");
            downloadSpeedSlot.PersistentSelf = false;
            _downloadSpeed = downloadSpeedSlot.AttachComponent<DynamicValueVariable<float>>();
            _downloadSpeed.VariableName.Value = $"World/Userlist-{userIndex}-downloadspeed";
            
            var uploadSpeedSlot = _networkSlot.AddSlot("UploadSpeed");
            uploadSpeedSlot.PersistentSelf = false;
            _uploadSpeed = uploadSpeedSlot.AttachComponent<DynamicValueVariable<float>>();
            _uploadSpeed.VariableName.Value = $"World/Userlist-{userIndex}-uploadspeed";
            
            var queuedPacketsSlot = _networkSlot.AddSlot("QueuedPackets");
            queuedPacketsSlot.PersistentSelf = false;
            _queuedPackets = queuedPacketsSlot.AttachComponent<DynamicValueVariable<int>>();
            _queuedPackets.VariableName.Value = $"World/Userlist-{userIndex}-packets";
        }
        
        private void CreatePresenceChildSlots()
        {
            var userIndex = _userIndex;
            
            // Create child slots for presence information
            var presentInWorldSlot = _presenceSlot.AddSlot("PresentInWorld");
            presentInWorldSlot.PersistentSelf = false;
            _presentInWorld = presentInWorldSlot.AttachComponent<DynamicValueVariable<bool>>();
            _presentInWorld.VariableName.Value = $"World/Userlist-{userIndex}-presentinworld";
            
            var presentInHeadsetSlot = _presenceSlot.AddSlot("PresentInHeadset");
            presentInHeadsetSlot.PersistentSelf = false;
            _presentInHeadset = presentInHeadsetSlot.AttachComponent<DynamicValueVariable<bool>>();
            _presentInHeadset.VariableName.Value = $"World/Userlist-{userIndex}-presentinheadset";
            
            var vrActiveSlot = _presenceSlot.AddSlot("VRActive");
            vrActiveSlot.PersistentSelf = false;
            _vrActive = vrActiveSlot.AttachComponent<DynamicValueVariable<bool>>();
            _vrActive.VariableName.Value = $"World/Userlist-{userIndex}-vractive";
            
            var isMutedSlot = _presenceSlot.AddSlot("IsMuted");
            isMutedSlot.PersistentSelf = false;
            _isMuted = isMutedSlot.AttachComponent<DynamicValueVariable<bool>>();
            _isMuted.VariableName.Value = $"World/Userlist-{userIndex}-muted";
            
            var voiceModeSlot = _presenceSlot.AddSlot("VoiceMode");
            voiceModeSlot.PersistentSelf = false;
            _voiceMode = voiceModeSlot.AttachComponent<DynamicValueVariable<string>>();
            _voiceMode.VariableName.Value = $"World/Userlist-{userIndex}-voicemode";
            
            var voiceStreamSlot = _presenceSlot.AddSlot("VoiceStream");
            voiceStreamSlot.PersistentSelf = false;
            _voiceStream = voiceStreamSlot.AttachComponent<DynamicReferenceVariable<AudioStream<MonoSample>>>();
            _voiceStream.VariableName.Value = $"World/Userlist-{userIndex}-voice";
        }
        
        private void CreateSessionChildSlots()
        {
            var userIndex = _userIndex;
            
            // Create child slots for session information
            var platformSlot = _sessionSlot.AddSlot("Platform");
            platformSlot.PersistentSelf = false;
            _platform = platformSlot.AttachComponent<DynamicValueVariable<string>>();
            _platform.VariableName.Value = $"World/Userlist-{userIndex}-platform";
            
            
            var isHostSlot = _sessionSlot.AddSlot("IsHost");
            isHostSlot.PersistentSelf = false;
            _isHost = isHostSlot.AttachComponent<DynamicValueVariable<bool>>();
            _isHost.VariableName.Value = $"World/Userlist-{userIndex}-host";
            
            var editModeSlot = _sessionSlot.AddSlot("EditMode");
            editModeSlot.PersistentSelf = false;
            _editMode = editModeSlot.AttachComponent<DynamicValueVariable<bool>>();
            _editMode.VariableName.Value = $"World/Userlist-{userIndex}-editmode";
        }
        
        private void CreateProfileChildSlots()
        {
            var userIndex = _userIndex;
            
            // Create child slots for profile information
            var headDeviceSlot = _profileSlot.AddSlot("HeadDevice");
            headDeviceSlot.PersistentSelf = false;
            _headDevice = headDeviceSlot.AttachComponent<DynamicValueVariable<string>>();
            _headDevice.VariableName.Value = $"World/Userlist-{userIndex}-headdevice";
            
            var primaryHandSlot = _profileSlot.AddSlot("PrimaryHand");
            primaryHandSlot.PersistentSelf = false;
            _primaryHand = primaryHandSlot.AttachComponent<DynamicValueVariable<string>>();
            _primaryHand.VariableName.Value = $"World/Userlist-{userIndex}-primaryhand";
            
            var badgesSlot = _profileSlot.AddSlot("Badges");
            badgesSlot.PersistentSelf = false;
            _badges = badgesSlot.AttachComponent<DynamicValueVariable<string>>();
            _badges.VariableName.Value = $"World/Userlist-{userIndex}-badges";
            
            // Badge count variable
            _badgeCount = badgesSlot.AttachComponent<DynamicValueVariable<int>>();
            _badgeCount.VariableName.Value = $"World/Userlist-{userIndex}-badges-count";
            _badgeCount.Value.Value = 0;
        }
        
        private void CreateTrackingChildSlots()
        {
            var userIndex = _userIndex; // Use user ID for uniqueness
            
            // Create child slots for tracking information
            var eyeTrackingSlot = _trackingSlot.AddSlot("EyeTracking");
            eyeTrackingSlot.PersistentSelf = false;
            _eyeTracking = eyeTrackingSlot.AttachComponent<DynamicValueVariable<bool>>();
            _eyeTracking.VariableName.Value = $"World/Userlist-{userIndex}-eyetracking";
            
            var pupilTrackingSlot = _trackingSlot.AddSlot("PupilTracking");
            pupilTrackingSlot.PersistentSelf = false;
            _pupilTracking = pupilTrackingSlot.AttachComponent<DynamicValueVariable<bool>>();
            _pupilTracking.VariableName.Value = $"World/Userlist-{userIndex}-pupiltracking";
            
            var mouthTrackingSlot = _trackingSlot.AddSlot("MouthTracking");
            mouthTrackingSlot.PersistentSelf = false;
            _mouthTracking = mouthTrackingSlot.AttachComponent<DynamicValueVariable<string>>();
            _mouthTracking.VariableName.Value = $"World/Userlist-{userIndex}-mouthtracking";
        }
        
        private void CreatePrivacyChildSlots()
        {
            var userIndex = _userIndex; // Use user ID for uniqueness
            
            // Create child slots for privacy settings
            var hideInScreenshotsSlot = _privacySlot.AddSlot("HideInScreenshots");
            hideInScreenshotsSlot.PersistentSelf = false;
            _hideInScreenshots = hideInScreenshotsSlot.AttachComponent<DynamicValueVariable<bool>>();
            _hideInScreenshots.VariableName.Value = $"World/Userlist-{userIndex}-hideinscreenshots";
            
            var mediaMetadataOptOutSlot = _privacySlot.AddSlot("MediaMetadataOptOut");
            mediaMetadataOptOutSlot.PersistentSelf = false;
            _mediaMetadataOptOut = mediaMetadataOptOutSlot.AttachComponent<DynamicValueVariable<bool>>();
            _mediaMetadataOptOut.VariableName.Value = $"World/Userlist-{userIndex}-mediametadataoptout";
            
            var utcOffsetSlot = _privacySlot.AddSlot("UTCOffset");
            utcOffsetSlot.PersistentSelf = false;
            _utcOffset = utcOffsetSlot.AttachComponent<DynamicValueVariable<string>>();
            _utcOffset.VariableName.Value = $"World/Userlist-{userIndex}-utcoffset";
        }
        
        private void CreateAccountChildSlots()
        {
            var userIndex = _userIndex;
            
            // Create CloudUserInfo component to fetch cloud data
            _cloudUserInfo = _accountSlot.AttachComponent<CloudUserInfo>();
            _cloudUserInfo.UserId.Value = _userId;
            
            // Create child slots for account information
            var registrationDateSlot = _accountSlot.AddSlot("RegistrationDate");
            registrationDateSlot.PersistentSelf = false;
            _registrationDate = registrationDateSlot.AttachComponent<DynamicValueVariable<string>>();
            _registrationDate.VariableName.Value = $"World/Userlist-{userIndex}-registrationdate";
            
            var accountAgeSlot = _accountSlot.AddSlot("AccountAge");
            accountAgeSlot.PersistentSelf = false;
            _accountAgeInDays = accountAgeSlot.AttachComponent<DynamicValueVariable<int>>();
            _accountAgeInDays.VariableName.Value = $"World/Userlist-{userIndex}-accountage";
            
            var birthdaySlot = _accountSlot.AddSlot("IsAccountBirthday");
            birthdaySlot.PersistentSelf = false;
            _isAccountBirthday = birthdaySlot.AttachComponent<DynamicValueVariable<bool>>();
            _isAccountBirthday.VariableName.Value = $"World/Userlist-{userIndex}-birthday";
            
            var profileIconSlot = _accountSlot.AddSlot("ProfileIconUrl");
            profileIconSlot.PersistentSelf = false;
            _profileIconUrl = profileIconSlot.AttachComponent<DynamicValueVariable<string>>();
            _profileIconUrl.VariableName.Value = $"World/Userlist-{userIndex}-profileicon";
        }
        
        private void UpdateSlotNames(FrooxEngine.User user)
        {
            // Update child slot names with formatting - categories keep simple names
            _performanceSlot.Name = "Performance";
            _networkSlot.Name = "Network";
            _presenceSlot.Name = "Presence";
            _sessionSlot.Name = "Session";
            _profileSlot.Name = "Profile";
            _trackingSlot.Name = "Tracking";
            _privacySlot.Name = "Privacy";
            _accountSlot.Name = "Account";
            
            UpdatePerformanceChildNames(user);
            UpdateNetworkChildNames(user);
            UpdatePresenceChildNames(user);
            UpdateSessionChildNames(user);
            UpdateProfileChildNames(user);
            UpdateTrackingChildNames(user);
            UpdatePrivacyChildNames(user);
            
            // Only update account names if cloud data is loaded
            if (_cloudUserInfo?.IsLoaded?.Value == true)
            {
                UpdateAccountChildNames(user);
            }
        }
        
        private void UpdatePerformanceChildNames(FrooxEngine.User user)
        {
            var fps = user.FPS;
            var fpsSlot = _performanceSlot.FindChild("FPS");
            if (fpsSlot != null)
            {
                fpsSlot.Name = $"<b>FPS</b> | {fps:F0}<size=50%>FPS</size>";
            }
        }
        
        private void UpdateNetworkChildNames(FrooxEngine.User user)
        {
            var ping = user.Ping;
            var queuedMessages = user.QueuedMessages;
            var packetLoss = user.PacketLoss;
            // Convert from bytes/s to MB/s for display (1 MB = 1,048,576 bytes)
            var downloadSpeedMB = user.DownloadSpeed / 1048576f;
            var uploadSpeedMB = user.UploadSpeed / 1048576f;
            
            var pingSlot = _networkSlot.FindChild("Ping");
            if (pingSlot != null)
                pingSlot.Name = $"<b>Ping</b> | {ping}<size=50%>MS</size>";
                
            var queuedSlot = _networkSlot.FindChild("QueuedPackets");
            if (queuedSlot != null)
                queuedSlot.Name = $"<b>Queued Packets</b> | {queuedMessages}<size=50%>PKT</size>";
                
            var packetLossSlot = _networkSlot.FindChild("PacketLoss");
            if (packetLossSlot != null)
                packetLossSlot.Name = $"<b>Packet Loss</b> | {packetLoss:F1}<size=50%>%</size>";
                
            var downloadSlot = _networkSlot.FindChild("DownloadSpeed");
            if (downloadSlot != null)
                downloadSlot.Name = $"<b>Download</b> | {downloadSpeedMB:F2}<size=50%>MB/s</size>";
                
            var uploadSlot = _networkSlot.FindChild("UploadSpeed");
            if (uploadSlot != null)
                uploadSlot.Name = $"<b>Upload</b> | {uploadSpeedMB:F2}<size=50%>MB/s</size>";
        }
        
        private void UpdatePresenceChildNames(FrooxEngine.User user)
        {
            var presentInWorld = user.IsPresentInWorld;
            var presentInHeadset = user.IsPresentInHeadset;
            var vrActive = user.VR_Active;
            var isMuted = (bool)user.isMuted;
            var voiceMode = user.VoiceMode.ToString();
            // Check current voice stream state from our reference
            var hasVoiceStream = _voiceStream.Reference.Target != null;
            
            var worldSlot = _presenceSlot.FindChild("PresentInWorld");
            if (worldSlot != null)
                worldSlot.Name = $"<b>In World</b> | {(presentInWorld ? "True" : "False")}";
                
            var headsetSlot = _presenceSlot.FindChild("PresentInHeadset");
            if (headsetSlot != null)
                headsetSlot.Name = $"<b>In Headset</b> | {(presentInHeadset ? "True" : "False")}";
                
            var vrSlot = _presenceSlot.FindChild("VRActive");
            if (vrSlot != null)
                vrSlot.Name = $"<b>VR Active</b> | {(vrActive ? "True" : "False")}";
                
            var mutedSlot = _presenceSlot.FindChild("IsMuted");
            if (mutedSlot != null)
                mutedSlot.Name = $"<b>Muted</b> | {(isMuted ? "True" : "False")}";
                
            var voiceModeSlot = _presenceSlot.FindChild("VoiceMode");
            if (voiceModeSlot != null)
                voiceModeSlot.Name = $"<b>Voice Mode</b> | <size=50%>{voiceMode}</size>";
                
            var voiceStreamSlot = _presenceSlot.FindChild("VoiceStream");
            if (voiceStreamSlot != null)
                voiceStreamSlot.Name = $"<b>Voice Stream</b> | {(hasVoiceStream ? "True" : "False")}";
        }
        
        private void UpdateSessionChildNames(FrooxEngine.User user)
        {
            var platform = user.Platform.ToString();
            var isHost = user.IsHost;
            var editMode = user.EditMode;
            
            var platformSlot = _sessionSlot.FindChild("Platform");
            if (platformSlot != null)
                platformSlot.Name = $"<b>Platform</b> | <size=50%>{platform}</size>";
                
            var hostSlot = _sessionSlot.FindChild("IsHost");
            if (hostSlot != null)
                hostSlot.Name = $"<b>Host</b> | {(isHost ? "True" : "False")}";
                
            var editSlot = _sessionSlot.FindChild("EditMode");
            if (editSlot != null)
                editSlot.Name = $"<b>Edit Mode</b> | {(editMode ? "True" : "False")}";
        }
        
        private void UpdateProfileChildNames(FrooxEngine.User user)
        {
            var headDevice = user.HeadDevice.ToString();
            var primaryHand = user.Primaryhand.ToString();
            var badges = GetUserBadges(user);
            var badgeCount = string.IsNullOrEmpty(badges) ? 0 : badges.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                
            var deviceSlot = _profileSlot.FindChild("HeadDevice");
            if (deviceSlot != null)
                deviceSlot.Name = $"<b>Head Device</b> | <size=50%>{headDevice}</size>";
                
            var handSlot = _profileSlot.FindChild("PrimaryHand");
            if (handSlot != null)
                handSlot.Name = $"<b>Primary Hand</b> | <size=50%>{primaryHand}</size>";
                
            var badgesSlot = _profileSlot.FindChild("Badges");
            if (badgesSlot != null)
                badgesSlot.Name = $"<b>Badges</b> | {badgeCount}<size=50%>COUNT</size>";
        }
        
        private void UpdateTrackingChildNames(FrooxEngine.User user)
        {
            var eyeTracking = user.EyeTracking;
            var pupilTracking = user.PupilTracking;
            var mouthTrackingList = user.MouthTrackingParameters.Select(p => p.ToString()).ToList();
            var mouthTracking = string.Join(", ", mouthTrackingList);
            
            var eyeSlot = _trackingSlot.FindChild("EyeTracking");
            if (eyeSlot != null)
                eyeSlot.Name = $"<b>Eye Tracking</b> | {(eyeTracking ? "True" : "False")}";
                
            var pupilSlot = _trackingSlot.FindChild("PupilTracking");
            if (pupilSlot != null)
                pupilSlot.Name = $"<b>Pupil Tracking</b> | {(pupilTracking ? "True" : "False")}";
                
            var mouthSlot = _trackingSlot.FindChild("MouthTracking");
            if (mouthSlot != null)
                mouthSlot.Name = $"<b>Mouth Tracking</b> | {(!string.IsNullOrEmpty(mouthTracking) ? "True" : "False")}";
        }
        
        private void UpdatePrivacyChildNames(FrooxEngine.User user)
        {
            var hideInScreenshots = user.HideInScreenshots;
            var mediaMetadataOptOut = user.MediaMetadataOptOut;
            var utcOffset = user.UTCOffset.ToString(@"hh\:mm");
            
            var screenshotSlot = _privacySlot.FindChild("HideInScreenshots");
            if (screenshotSlot != null)
                screenshotSlot.Name = $"<b>Hide in Screenshots</b> | {(hideInScreenshots ? "True" : "False")}";
                
            var metadataSlot = _privacySlot.FindChild("MediaMetadataOptOut");
            if (metadataSlot != null)
                metadataSlot.Name = $"<b>Metadata Opt-Out</b> | {(mediaMetadataOptOut ? "True" : "False")}";
                
            var offsetSlot = _privacySlot.FindChild("UTCOffset");
            if (offsetSlot != null)
                offsetSlot.Name = $"<b>UTC Offset</b> | <size=50%>{utcOffset}</size>";
        }
        
        private void UpdateAccountChildNames(FrooxEngine.User user)
        {
            try
            {
                // Get data directly from CloudUserInfo if loaded, otherwise use default values
                string registrationDate;
                int accountAge;
                bool isAccountBirthday;
                string profileIconUrl;
                
                if (_cloudUserInfo?.IsLoaded?.Value == true)
                {
                    var regDate = _cloudUserInfo.RegistrationDate.Value;
                    var now = DateTime.UtcNow;
                    
                    registrationDate = regDate.ToString("yyyy-MM-dd");
                    accountAge = (int)(now - regDate).TotalDays;
                    isAccountBirthday = now.Month == regDate.Month && now.Day == regDate.Day;
                    profileIconUrl = _cloudUserInfo.IconURL.Value?.ToString() ?? "";
                }
                else
                {
                    registrationDate = _cloudUserInfo?.IsLoaded?.Value == false ? "Loading..." : UNKNOWN_VALUE;
                    accountAge = 0;
                    isAccountBirthday = false;
                    profileIconUrl = "";
                }
                
                var regDateSlot = _accountSlot.FindChild("RegistrationDate");
                if (regDateSlot != null)
                    regDateSlot.Name = $"<b>Registration</b> | <size=50%>{registrationDate}</size>";
                    
                var ageSlot = _accountSlot.FindChild("AccountAge");
                if (ageSlot != null)
                    ageSlot.Name = $"<b>Account Age</b> | {accountAge}<size=50%>DAYS</size>";
                    
                var birthdaySlot = _accountSlot.FindChild("IsAccountBirthday");
                if (birthdaySlot != null)
                    birthdaySlot.Name = $"<b>Birthday</b> | {(isAccountBirthday ? "True" : "False")}";
                    
                var iconSlot = _accountSlot.FindChild("ProfileIconUrl");
                if (iconSlot != null)
                    iconSlot.Name = $"<b>Profile Icon</b> | {(!string.IsNullOrEmpty(profileIconUrl) ? "True" : "False")}";
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to update account child names: {ex.Message}");
            }
        }
        
        private void UpdateAccountInformation()
        {
            try
            {
                var wasLoaded = _lastCloudDataLoaded;
                var isNowLoaded = _cloudUserInfo?.IsLoaded?.Value == true;
                
                // Check if CloudUserInfo component has loaded data
                if (isNowLoaded)
                {
                    var registrationDate = _cloudUserInfo.RegistrationDate.Value;
                    var now = DateTime.UtcNow;
                    
                    // Update registration date
                    _registrationDate.Value.Value = registrationDate.ToString("yyyy-MM-dd");
                    
                    // Calculate account age in days
                    var accountAge = (int)(now - registrationDate).TotalDays;
                    _accountAgeInDays.Value.Value = accountAge;
                    
                    // Check if it's their account birthday (anniversary)
                    var isBirthday = now.Month == registrationDate.Month && now.Day == registrationDate.Day;
                    _isAccountBirthday.Value.Value = isBirthday;
                    
                    // Update profile icon URL
                    _profileIconUrl.Value.Value = _cloudUserInfo.IconURL.Value?.ToString() ?? "";
                    
                    // If data just loaded, trigger slot name update
                    if (!wasLoaded && isNowLoaded && _lastUser != null)
                    {
                        UpdateAccountChildNames(_lastUser);
                    }
                }
                else
                {
                    // CloudUserInfo not loaded yet or failed to load
                    _registrationDate.Value.Value = _cloudUserInfo?.IsLoaded?.Value == false ? "Loading..." : UNKNOWN_VALUE;
                    _accountAgeInDays.Value.Value = 0;
                    _isAccountBirthday.Value.Value = false;
                    _profileIconUrl.Value.Value = "";
                }
                
                _lastCloudDataLoaded = isNowLoaded;
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to update account information: {ex.Message}");
                _registrationDate.Value.Value = CLOUD_DATA_FETCH_ERROR_MESSAGE;
                _accountAgeInDays.Value.Value = 0;
                _isAccountBirthday.Value.Value = false;
                _profileIconUrl.Value.Value = "";
            }
        }

        public void UpdateUserInfo(FrooxEngine.User user, bool updateSlotNames = true)
        {
            try
            {
                _lastUser = user; // Store for potential account slot name updates
                
                // Update user reference and name
                _userReference.Reference.Target = user;
                var userName = user.UserName ?? UNKNOWN_VALUE;
                _userName.Value.Value = userName;
                
                // Update the actual slot name to reflect current username
                _userSlot.Name = userName;

                // Update FPS - only if changed significantly
                var fps = user.FPS;
                if (Math.Abs(_lastFps - fps) > 0.1f)
                {
                    _fpsValue.Value.Value = fps;
                    _fpsString.Value.Value = $"{fps:F1} FPS";
                    _lastFps = fps;
                }

                // Update queued packets - only if changed
                var queuedMessages = user.QueuedMessages;
                if (_lastQueuedPackets != queuedMessages)
                {
                    _queuedPackets.Value.Value = queuedMessages;
                    _lastQueuedPackets = queuedMessages;
                }

                // Update badges - only if changed
                var badges = GetUserBadges(user);
                if (_lastBadges != badges)
                {
                    _badges.Value.Value = badges;
                    _lastBadges = badges;
                }

                // Update voice stream - keep checking until found, or if it becomes null again
                if (!_voiceStreamFound || _voiceStream.Reference.Target == null)
                {
                    var voiceStream = GetUserVoiceStream(user);
                    if (voiceStream != null)
                    {
                        _voiceStream.Reference.Target = voiceStream;
                        if (!_voiceStreamFound)
                        {
                            _voiceStreamFound = true; // Stop checking once found
                            UniLog.Log($"Voice stream found for user {user.UserName}");
                        }
                    }
                    else if (_voiceStreamFound && _voiceStream.Reference.Target == null)
                    {
                        // Voice stream was lost, restart detection
                        _voiceStreamFound = false;
                        UniLog.Log($"Voice stream lost for user {user.UserName}, restarting detection");
                    }
                }
                
                // Update network information - only if changed
                var ping = user.Ping;
                if (_lastPing != ping)
                {
                    _ping.Value.Value = ping;
                    _lastPing = ping;
                }
                
                var packetLoss = user.PacketLoss;
                if (Math.Abs(_lastPacketLoss - packetLoss) > 0.01f)
                {
                    _packetLoss.Value.Value = packetLoss;
                    _lastPacketLoss = packetLoss;
                }
                
                // Convert from bytes/s to MB/s (1 MB = 1,048,576 bytes)
                _downloadSpeed.Value.Value = user.DownloadSpeed / 1048576f;
                _uploadSpeed.Value.Value = user.UploadSpeed / 1048576f;
                
                // Update presence information - only if changed
                var presentInWorld = user.IsPresentInWorld;
                if (_lastPresentInWorld != presentInWorld)
                {
                    _presentInWorld.Value.Value = presentInWorld;
                    _lastPresentInWorld = presentInWorld;
                }
                
                var presentInHeadset = user.IsPresentInHeadset;
                if (_lastPresentInHeadset != presentInHeadset)
                {
                    _presentInHeadset.Value.Value = presentInHeadset;
                    _lastPresentInHeadset = presentInHeadset;
                }
                
                var vrActive = user.VR_Active;
                if (_lastVrActive != vrActive)
                {
                    _vrActive.Value.Value = vrActive;
                    _lastVrActive = vrActive;
                }
                
                var isMuted = (bool)user.isMuted;
                if (_lastIsMuted != isMuted)
                {
                    _isMuted.Value.Value = isMuted;
                    _lastIsMuted = isMuted;
                }
                
                _voiceMode.Value.Value = user.VoiceMode.ToString();
                
                // Update session information - only if changed
                var platform = user.Platform.ToString();
                if (_lastPlatform != platform)
                {
                    _platform.Value.Value = platform;
                    _lastPlatform = platform;
                }
                
                
                var isHost = user.IsHost;
                if (_lastIsHost != isHost)
                {
                    _isHost.Value.Value = isHost;
                    _lastIsHost = isHost;
                }
                
                var editMode = user.EditMode;
                if (_lastEditMode != editMode)
                {
                    _editMode.Value.Value = editMode;
                    _lastEditMode = editMode;
                }
                
                // Update profile information
                _headDevice.Value.Value = user.HeadDevice.ToString();
                _primaryHand.Value.Value = user.Primaryhand.ToString();
                
                // Update tracking information
                _eyeTracking.Value.Value = user.EyeTracking;
                _pupilTracking.Value.Value = user.PupilTracking;
                var mouthTrackingList = user.MouthTrackingParameters.Select(p => p.ToString()).ToList();
                _mouthTracking.Value.Value = string.Join(", ", mouthTrackingList);
                
                // Update privacy information
                _hideInScreenshots.Value.Value = user.HideInScreenshots;
                _mediaMetadataOptOut.Value.Value = user.MediaMetadataOptOut;
                _utcOffset.Value.Value = user.UTCOffset.ToString(@"hh\:mm");
                
                // Update account information from CloudUserInfo
                UpdateAccountInformation();
                
                // Update slot names only when requested (less frequent for performance)
                if (updateSlotNames)
                {
                    UpdateSlotNames(user);
                }
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to update user info for {user.UserName}: {ex.Message}");
            }
        }

        private string GetUserBadges(FrooxEngine.User user)
        {
            var badges = new List<(string name, Uri? url)>();
            
            // Basic badges based on user properties
            if (user.IsHost)
                badges.Add(("Host", OfficialAssets.Graphics.Badges.Host));
            
            if (user.IsPatron)
                badges.Add(("Patron", null)); // No specific URL found
                
            if (user.Platform.IsMobilePlatform())
                badges.Add(("Mobile", OfficialAssets.Graphics.Badges.Mobile));
                
            if (user.Platform == Platform.Linux)
                badges.Add(("Linux", OfficialAssets.Graphics.Badges.Linux));

            // Get comprehensive badge list from cloud user data
            if (_cloudUserInfo?.IsLoaded?.Value == true)
            {
                try
                {
                    // Access cloud user data via reflection since CloudUser property might be internal
                    var cloudUserProperty = _cloudUserInfo.GetType().GetProperty("CloudUser", 
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (cloudUserProperty?.GetValue(_cloudUserInfo) is SkyFrost.Base.User cloudUserData && cloudUserData.Tags != null)
                    {
                        // Add all cloud badges based on tags
                        foreach (var tag in cloudUserData.Tags)
                        {
                            if (string.IsNullOrEmpty(tag)) continue;
                            
                            // Convert specific known tags to readable names and URLs
                            var (badgeName, badgeUrl) = ConvertTagToBadgeInfo(tag);
                            if (!badges.Any(b => b.name == badgeName))
                            {
                                badges.Add((badgeName, badgeUrl));
                            }
                        }
                        
                        // Add special badges
                        if (cloudUserData.HasSupported)
                            badges.Add(("Supporter", OfficialAssets.Graphics.Badges.Supporter));
                    }
                }
                catch (Exception ex)
                {
                    UniLog.Warning($"Failed to get cloud badges for {user.UserName}: {ex.Message}");
                }
            }

            // Enhanced: Get badges directly from AvatarManager BadgeTemplates
            var userRoot = user.Root;
            if (userRoot != null)
            {
                var avatarManager = userRoot.GetRegisteredComponent<AvatarManager>();
                if (avatarManager?.BadgeTemplates != null)
                {
                    // Access badge templates directly - much more reliable than parsing nameplate
                    foreach (var badgeSlot in avatarManager.BadgeTemplates.Children)
                    {
                        if (badgeSlot != null && !string.IsNullOrEmpty(badgeSlot.Name))
                        {
                            var badgeName = badgeSlot.Name;
                            
                            // Try to get the badge URL from the slot's texture components
                            Uri? badgeUrl = GetBadgeUrlFromSlot(badgeSlot);
                            
                            // Convert badge name to a readable format and add if not already present
                            var readableName = ConvertBadgeSlotNameToReadable(badgeName);
                            if (!badges.Any(b => b.name.Equals(readableName, StringComparison.OrdinalIgnoreCase)))
                            {
                                badges.Add((readableName, badgeUrl));
                            }
                        }
                    }
                }
                
                // Fallback: Parse nameplate if BadgeTemplates is empty or not accessible
                if (badges.Count <= 4 && avatarManager != null) // Only basic badges found
                {
                    var nameTagText = avatarManager.NameTagText?.Value;
                    if (!string.IsNullOrEmpty(nameTagText) && nameTagText.Contains("<sprite"))
                    {
                        var spriteTags = System.Text.RegularExpressions.Regex.Matches(nameTagText, @"<sprite name=""([^""]+)"">");
                        foreach (System.Text.RegularExpressions.Match match in spriteTags)
                        {
                            if (match.Groups.Count > 1)
                            {
                                var spriteName = match.Groups[1].Value;
                                var readableName = ConvertBadgeSlotNameToReadable(spriteName);
                                if (!badges.Any(b => b.name.Equals(readableName, StringComparison.OrdinalIgnoreCase)))
                                {
                                    badges.Add((readableName, null));
                                }
                            }
                        }
                    }
                }
            }

            // Update badge URL dynamic variables after all badges are collected
            UpdateBadgeUrls(badges);
            
            // Update badge count
            if (_badgeCount != null)
            {
                _badgeCount.Value.Value = badges.Count;
            }

            return string.Join(", ", badges.Select(b => b.name));
        }
        
        private (string name, Uri? url) ConvertTagToBadgeInfo(string tag)
        {
            // Convert known tags to readable badge names and URLs
            return tag switch
            {
                "team" => ("Team", OfficialAssets.Graphics.Badges.Team),
                "moderator" => ("Moderator", OfficialAssets.Graphics.Badges.Moderator), 
                "mentor" => ("Mentor", OfficialAssets.Graphics.Badges.Mentor),
                "translator" => ("Translator", OfficialAssets.Graphics.Badges.Translator),
                "hearing_impaired" => ("Hearing Impaired", OfficialAssets.Graphics.Badges.HearingImpaired),
                "visually_impaired" => ("Visually Impaired", OfficialAssets.Graphics.Badges.VisionImpaired),
                "color_vision_deficiency" => ("Color Blind", OfficialAssets.Graphics.Badges.ColorVisionDeficiency),
                "speech_impaired" => ("Speech Impaired", OfficialAssets.Graphics.Badges.SpeechImpaired),
                "potato" => ("Potato", OfficialAssets.Graphics.Badges.potato),
                
                // MMC badges
                "mmc_participant" => ("MMC Participant", OfficialAssets.Graphics.Badges.MMC._128px.Participation),
                "mmc_cow" => ("MMC Cow", OfficialAssets.Graphics.Badges.MMC._128px.Cow),
                "mmc_lips" => ("MMC Lips", OfficialAssets.Graphics.Badges.MMC._128px.Lips),
                
                // MMC21 badges
                "mmc21_participant" => ("MMC21 Participant", OfficialAssets.Graphics.Badges.MMC21.MMC_participation),
                "mmc21_smile" => ("MMC21 Smile", OfficialAssets.Graphics.Badges.MMC21.SmileBadge),
                "mmc21_mouth" => ("MMC21 Mouth", OfficialAssets.Graphics.Badges.MMC21.MMC2021Mouth),
                "mmc21_grillcheeze" => ("MMC21 Grill Cheeze", OfficialAssets.Graphics.Badges.MMC21.GrillCheeze),
                
                // MMC22 badges
                "mmc22_participant" => ("MMC22 Participant", OfficialAssets.Graphics.Badges.MMC22.Participation),
                "mmc22_honorablemention" => ("MMC22 Honorable Mention", OfficialAssets.Graphics.Badges.MMC22.HonorableMention),
                "mmc22_world" => ("MMC22 World Winner", OfficialAssets.Graphics.Badges.MMC22.World),
                "mmc22_avatar" => ("MMC22 Avatar Winner", OfficialAssets.Graphics.Badges.MMC22.Avatar),
                "mmc22_art" => ("MMC22 Art Winner", OfficialAssets.Graphics.Badges.MMC22.Art),
                "mmc22_esd" => ("MMC22 ESD Winner", OfficialAssets.Graphics.Badges.MMC22.ESD),
                "mmc22_meme" => ("MMC22 Meme Winner", OfficialAssets.Graphics.Badges.MMC22.Meme),
                "mmc22_other" => ("MMC22 Other Winner", OfficialAssets.Graphics.Badges.MMC22.Other),
                "mmc22_cheesecoin" => ("MMC22 CheeseCoin", OfficialAssets.Graphics.Badges.MMC22.CheeseCoin),
                "mmc22_litalita" => ("MMC22 LitaLita", OfficialAssets.Graphics.Badges.MMC22.Litalita),
                "mmc22_holywater" => ("MMC22 HolyWater", OfficialAssets.Graphics.Badges.MMC22.HolyWater),
                
                // MMC23 badges  
                "mmc23_participant" => ("MMC23 Participant", OfficialAssets.Graphics.Badges.MMC23.Participation),
                "mmc23_honorablemention" => ("MMC23 Honorable Mention", OfficialAssets.Graphics.Badges.MMC23.HonorableMention),
                "mmc23_world" => ("MMC23 World Winner", OfficialAssets.Graphics.Badges.MMC23.World),
                "mmc23_avatar" => ("MMC23 Avatar Winner", OfficialAssets.Graphics.Badges.MMC23.Avatar),
                "mmc23_art" => ("MMC23 Art Winner", OfficialAssets.Graphics.Badges.MMC23.Art),
                "mmc23_esd" => ("MMC23 ESD Winner", OfficialAssets.Graphics.Badges.MMC23.ESD),
                "mmc23_meme" => ("MMC23 Meme Winner", OfficialAssets.Graphics.Badges.MMC23.Meme),
                "mmc23_other" => ("MMC23 Other Winner", OfficialAssets.Graphics.Badges.MMC23.Other),
                "mmc23_gifty" => ("MMC23 Gifty", OfficialAssets.Graphics.Badges.MMC23.GiftyMMC),
                "mmc23_prime" => ("MMC23 Prime", OfficialAssets.Graphics.Badges.MMC23.Prime_MMC23),
                "mmc23_litalita" => ("MMC23 LitaLita", OfficialAssets.Graphics.Badges.MMC23.litalita_MMC23),
                "mmc23_holywater" => ("MMC23 HolyWater", OfficialAssets.Graphics.Badges.MMC23.Holy_Water_MMC23),
                
                // MMC24 badges
                "mmc24_participant" => ("MMC24 Participant", OfficialAssets.Graphics.Badges.MMC24.Participation_Badge),
                "mmc24_honorablemention" => ("MMC24 Honorable Mention", OfficialAssets.Graphics.Badges.MMC24.HonorableMention), 
                "mmc24_world" => ("MMC24 World Winner", OfficialAssets.Graphics.Badges.MMC24.World),
                "mmc24_avatar" => ("MMC24 Avatar Winner", OfficialAssets.Graphics.Badges.MMC24.Avatar),
                "mmc24_art" => ("MMC24 Art Winner", OfficialAssets.Graphics.Badges.MMC24.Art),
                "mmc24_esd" => ("MMC24 ESD Winner", OfficialAssets.Graphics.Badges.MMC24.ESD),
                "mmc24_meme" => ("MMC24 Meme Winner", OfficialAssets.Graphics.Badges.MMC24.Meme),
                "mmc24_other" => ("MMC24 Other Winner", OfficialAssets.Graphics.Badges.MMC24.Other),
                "mmc24_narrative" => ("MMC24 Narrative Winner", OfficialAssets.Graphics.Badges.MMC24.Narrative),
                "mmc24_gifty" => ("MMC24 Gifty", OfficialAssets.Graphics.Badges.MMC24.GiftyMMC24),
                "mmc24_doggy" => ("MMC24 Doggy", OfficialAssets.Graphics.Badges.MMC24.ProbablePrime_MMC24_doggy),
                "mmc24_holywater" => ("MMC24 HolyWater", OfficialAssets.Graphics.Badges.MMC24.Holy_Water_MMC24),
                "mmc24_litalita" => ("MMC24 LitaLita", OfficialAssets.Graphics.Badges.MMC24.litalita_MMC24),
                "mmc24_froox" => ("MMC24 Froox", OfficialAssets.Graphics.Badges.MMC24.Frooxius_Logo_A),
                
                // MMC25 badges - The complete set!
                "mmc25_participant" => ("MMC25 Participant", OfficialAssets.Graphics.Badges.MMC25.Participation),
                "mmc25_honorablemention" => ("MMC25 Honorable Mention", OfficialAssets.Graphics.Badges.MMC25.HonorableMention),
                "mmc25_world" => ("MMC25 World Winner", OfficialAssets.Graphics.Badges.MMC25.World), 
                "mmc25_avatar" => ("MMC25 Avatar Winner", OfficialAssets.Graphics.Badges.MMC25.Avatar),
                "mmc25_art" => ("MMC25 Art Winner", OfficialAssets.Graphics.Badges.MMC25.Art),
                "mmc25_esd" => ("MMC25 ESD Winner", OfficialAssets.Graphics.Badges.MMC25.ESD),
                "mmc25_meme" => ("MMC25 Meme Winner", OfficialAssets.Graphics.Badges.MMC25.Meme),
                "mmc25_narrative" => ("MMC25 Narrative Winner", OfficialAssets.Graphics.Badges.MMC25.Narrative),
                "mmc25_other" => ("MMC25 Other Winner", OfficialAssets.Graphics.Badges.MMC25.Other),
                "mmc25_gifty" => ("MMC25 Gifty", OfficialAssets.Graphics.Badges.MMC25.GiftyMMC25), // THE badge you wanted!
                "mmc25_volunteer_bun" => ("MMC25 Volunteer Bun", OfficialAssets.Graphics.Badges.MMC25.Volunteer_Bun),
                "mmc25_volunteer_rib" => ("MMC25 Volunteer Rib", OfficialAssets.Graphics.Badges.MMC25.Volunteer_Rib),
                
                // Default: return the tag as-is with some formatting
                _ => (tag.Replace("_", " ").Replace("mmc", "MMC").Replace("Mmc", "MMC"), null)
            };
        }
        
        private void UpdateBadgeUrls(List<(string name, Uri? url)> badges)
        {
            try
            {
                // Clear existing badge URL slots that are no longer needed
                while (_badgeUrls.Count > badges.Count)
                {
                    var lastIndex = _badgeUrls.Count - 1;
                    _badgeUrls[lastIndex].Slot?.Destroy();
                    _badgeUrls.RemoveAt(lastIndex);
                }
                
                // Update or create badge URL dynamic variables
                for (int i = 0; i < badges.Count; i++)
                {
                    var (badgeName, badgeUrl) = badges[i];
                    
                    if (i >= _badgeUrls.Count)
                    {
                        // Create new badge URL slot
                        var badgeSlot = _profileSlot.FindChild("Badges")?.AddSlot($"Badge{i + 1}");
                        if (badgeSlot != null)
                        {
                            badgeSlot.PersistentSelf = false;
                            var badgeUrlVar = badgeSlot.AttachComponent<DynamicValueVariable<Uri>>();
                            badgeUrlVar.VariableName.Value = $"World/Userlist-{_userIndex}-badges-{i + 1}-url";
                            _badgeUrls.Add(badgeUrlVar);
                        }
                    }
                    
                    // Update the badge URL and slot name
                    if (i < _badgeUrls.Count)
                    {
                        _badgeUrls[i].Value.Value = badgeUrl;
                        if (_badgeUrls[i].Slot != null)
                        {
                            _badgeUrls[i].Slot.Name = $"<b>{badgeName}</b> | {(badgeUrl != null ? "True" : "False")}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to update badge URLs: {ex.Message}");
            }
        }
        
        private Uri? GetBadgeUrlFromSlot(Slot badgeSlot)
        {
            try
            {
                // Look for StaticTexture2D components that contain the badge image
                var staticTexture = badgeSlot.GetComponent<StaticTexture2D>();
                if (staticTexture?.URL != null)
                {
                    return staticTexture.URL.Value;
                }
                
                // Look for UnlitMaterial with texture
                var unlitMaterial = badgeSlot.GetComponent<UnlitMaterial>();
                if (unlitMaterial?.Texture?.Target is StaticTexture2D texture && texture.URL != null)
                {
                    return texture.URL.Value;
                }
                
                // Look in children for texture components
                foreach (var child in badgeSlot.Children)
                {
                    var childTexture = child.GetComponent<StaticTexture2D>();
                    if (childTexture?.URL != null)
                    {
                        return childTexture.URL.Value;
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to get badge URL from slot {badgeSlot.Name}: {ex.Message}");
                return null;
            }
        }
        
        private string ConvertBadgeSlotNameToReadable(string slotName)
        {
            // Convert badge slot names to readable format
            // Badge slot names in Resonite are usually just the badge name (e.g., "Host", "Team", "MMC25 Gifty")
            return slotName switch
            {
                "host" => "Host",
                "mobile" => "Mobile", 
                "linux" => "Linux",
                "team" => "Team",
                "moderator" => "Moderator",
                "mentor" => "Mentor",
                "translator" => "Translator",
                "hearing_impaired" => "Hearing Impaired",
                "visually_impaired" => "Visually Impaired",
                "color_vision_deficiency" => "Color Blind",
                "speech_impaired" => "Speech Impaired",
                "supporter" => "Supporter",
                "baguette" => "Baguette",
                "potato" => "Potato",
                
                // If it's already a readable name (like from BadgeTemplates), return as-is
                _ => slotName
            };
        }

        private AudioStream<MonoSample>? GetUserVoiceStream(FrooxEngine.User user)
        {
            try
            {
                // Try to find the user's voice stream using UserAudioStream component
                var userRoot = user.Root;
                if (userRoot?.Slot != null)
                {
                    var userAudioStream = userRoot.Slot.GetComponent<UserAudioStream<MonoSample>>();
                    if (userAudioStream?.Stream?.Target != null)
                    {
                        return userAudioStream.Stream.Target;
                    }
                }
                
                // Fallback: return null if no voice stream found
                return null;
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to get voice stream for {user.UserName}: {ex.Message}");
                return null;
            }
        }

        public void CleanupSlot()
        {
            try
            {
                _userSlot?.Destroy();
                
                // Reset tracking variables
                _voiceStreamFound = false;
                _lastCloudDataLoaded = false;
                _lastUser = null;
                
                // Clear badge URLs
                _badgeUrls.Clear();
            }
            catch (Exception ex)
            {
                UniLog.Warning($"Failed to cleanup user slot for {_userId}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Simple component that users can attach to manually reload UserList.
    /// Attach this to any slot and press the ReloadUserList button.
    /// </summary>
    [Category(new string[] {"Users"})]
    public class UserListReloader : Component
    {
        [SyncMethod(typeof(Action))]
        public void ReloadUserList()
        {
            UniLog.Log("UserList manual reload triggered");
            UserListManager.ReloadAll();
        }

        [SyncMethod(typeof(Action))] 
        public void ReloadMyUserList()
        {
            var userRoot = this.LocalUser?.Root;
            if (userRoot != null)
            {
                UniLog.Log($"Manual reload for {this.LocalUser?.UserName}");
                UserListManager.ReloadForUser(userRoot);
            }
            else
            {
                UniLog.Warning("Could not find UserRoot for reload");
            }
        }

        protected override void InitializeSyncMembers()
        {
            base.InitializeSyncMembers();
        }

        public static UserListReloader __New() => new();
    }
}