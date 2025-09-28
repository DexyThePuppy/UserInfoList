# UserInfoList Mod for Resonite

The mod creates a detailed hierarchical slot structure that exposes comprehensive user information as dynamic variables:

```
UserRoot
└── UserList
    ├── UserCount (dynamic variable with total user count)
    └── User1 (slot name shows actual username)
        ├── Performance
        │   └── FPS
        ├── Network
        │   ├── Ping
        │   ├── PacketLoss
        │   ├── DownloadSpeed (MB/s)
        │   ├── UploadSpeed (MB/s)
        │   └── QueuedPackets
        ├── Presence
        │   ├── PresentInWorld
        │   ├── PresentInHeadset
        │   ├── VRActive
        │   ├── IsMuted
        │   ├── VoiceMode
        │   └── VoiceStream
        ├── Session
        │   ├── Platform
        │   ├── IsHost
        │   └── EditMode
        ├── Profile
        │   ├── HeadDevice
        │   ├── PrimaryHand
        │   └── Badges (with individual badge URLs & count)
        ├── Tracking
        │   ├── EyeTracking
        │   ├── PupilTracking
        │   └── MouthTracking
        ├── Privacy
        │   ├── HideInScreenshots
        │   ├── MediaMetadataOptOut
        │   └── UTCOffset
        └── Account (Cloud-based information)
            ├── RegistrationDate
            ├── AccountAge (in days)
            ├── IsAccountBirthday
            └── ProfileIconUrl
```

## Dynamic Variables Created

### Global Variables
- `World/Userlist-UserCount` - DynamicValueVariable<int> containing total number of users

### Per-User Variables (using User1, User2, etc. numbering)

**Core User Information:**
- `World/Userlist-1` - DynamicReferenceVariable<User> pointing to the user object
- `World/Userlist-1-name` - DynamicValueVariable<string> containing the username

**Performance Information:**
- `World/Userlist-1-fps` - DynamicValueVariable<float> containing the user's FPS value
- `World/Userlist-1-fps-string` - DynamicValueVariable<string> containing formatted FPS display

**Network Information:**
- `World/Userlist-1-ping` - DynamicValueVariable<int> containing ping in milliseconds
- `World/Userlist-1-packetloss` - DynamicValueVariable<float> containing packet loss percentage
- `World/Userlist-1-downloadspeed` - DynamicValueVariable<float> containing download speed in MB/s
- `World/Userlist-1-uploadspeed` - DynamicValueVariable<float> containing upload speed in MB/s
- `World/Userlist-1-packets` - DynamicValueVariable<int> containing queued network packets

**Presence Information:**
- `World/Userlist-1-presentinworld` - DynamicValueVariable<bool> whether user is present in the world
- `World/Userlist-1-presentinheadset` - DynamicValueVariable<bool> whether user is in VR headset
- `World/Userlist-1-vractive` - DynamicValueVariable<bool> whether VR is active
- `World/Userlist-1-muted` - DynamicValueVariable<bool> whether user is muted
- `World/Userlist-1-voicemode` - DynamicValueVariable<string> containing voice mode (Normal, Mute, etc.)
- `World/Userlist-1-voice` - DynamicReferenceVariable<AudioStream<MonoSample>> pointing to the user's voice stream

**Session Information:**
- `World/Userlist-1-platform` - DynamicValueVariable<string> containing user's platform (PC, Mobile, etc.)
- `World/Userlist-1-host` - DynamicValueVariable<bool> whether user is the host
- `World/Userlist-1-editmode` - DynamicValueVariable<bool> whether user is in edit mode

**Profile Information:**
- `World/Userlist-1-headdevice` - DynamicValueVariable<string> containing head device type
- `World/Userlist-1-primaryhand` - DynamicValueVariable<string> containing primary hand preference
- `World/Userlist-1-badges` - DynamicValueVariable<string> containing comma-separated list of user badges
- `World/Userlist-1-badges-count` - DynamicValueVariable<int> containing number of badges
- `World/Userlist-1-badges-1-url` - DynamicValueVariable<Uri> containing first badge image URL
- `World/Userlist-1-badges-2-url` - DynamicValueVariable<Uri> containing second badge image URL
- *(Additional badge URLs created dynamically as needed)*

**Tracking Information:**
- `World/Userlist-1-eyetracking` - DynamicValueVariable<bool> whether eye tracking is active
- `World/Userlist-1-pupiltracking` - DynamicValueVariable<bool> whether pupil tracking is active
- `World/Userlist-1-mouthtracking` - DynamicValueVariable<string> containing mouth tracking parameters

**Privacy Information:**
- `World/Userlist-1-hideinscreenshots` - DynamicValueVariable<bool> whether user hides in screenshots
- `World/Userlist-1-mediametadataoptout` - DynamicValueVariable<bool> whether user opted out of media metadata
- `World/Userlist-1-utcoffset` - DynamicValueVariable<string> containing user's UTC offset

**Account Information (Cloud-based):**
- `World/Userlist-1-registrationdate` - DynamicValueVariable<string> containing account registration date
- `World/Userlist-1-accountage` - DynamicValueVariable<int> containing account age in days
- `World/Userlist-1-birthday` - DynamicValueVariable<bool> whether it's the user's account birthday
- `World/Userlist-1-profileicon` - DynamicValueVariable<string> containing profile icon URL
