# UserInfoList Mod for Resonite

This Harmony mod adds comprehensive user information tracking to Resonite by automatically creating a "UserList" slot structure under every UserRoot component.

## Features

The mod creates a hierarchical slot structure that exposes user information as dynamic variables:

```
UserRoot
└── UserList
    └── User1 (slot name shows actual username)
        ├── FPS
        ├── Queued Packets  
        ├── Badges
        ├── Voice
        ├── Network
        ├── Presence
        └── Session
    └── User2 (slot name shows actual username)
        ├── FPS
        ├── Queued Packets  
        ├── Badges
        ├── Voice
        ├── Network
        ├── Presence
        └── Session
```

### Dynamic Variables Created

For each user, the following dynamic variables are created (using User1, User2, etc. numbering):

**User Information:**
- `World/Userlist-User1` - DynamicReferenceVariable<User> pointing to the user object
- `World/Userlist-User1-name` - DynamicValueVariable<string> containing the username

**FPS Information:**
- `World/Userlist-User1-fps` - DynamicValueVariable<float> containing the user's FPS value
- `World/Userlist-User1-fps-string` - DynamicValueVariable<string> containing formatted FPS display

**Network Information:**
- `World/Userlist-User1-packets` - DynamicValueVariable<int> containing queued network packets

**Badges:**
- `World/Userlist-User1-badges` - DynamicValueVariable<string> containing comma-separated list of user badges

**Voice:**
- `World/Userlist-User1-voice` - DynamicReferenceVariable<IStream> pointing to the user's voice stream (if available)

**Network Information:**
- `World/Userlist-User1-ping` - DynamicValueVariable<int> containing the user's ping in milliseconds
- `World/Userlist-User1-packetloss` - DynamicValueVariable<float> containing packet loss percentage
- `World/Userlist-User1-downloadspeed` - DynamicValueVariable<float> containing download speed
- `World/Userlist-User1-uploadspeed` - DynamicValueVariable<float> containing upload speed

**Presence Information:**
- `World/Userlist-User1-presentinworld` - DynamicValueVariable<bool> whether user is present in the world
- `World/Userlist-User1-presentinheadset` - DynamicValueVariable<bool> whether user is in VR headset
- `World/Userlist-User1-vractive` - DynamicValueVariable<bool> whether VR is active
- `World/Userlist-User1-muted` - DynamicValueVariable<bool> whether user is muted
- `World/Userlist-User1-voicemode` - DynamicValueVariable<string> containing voice mode (Normal, Mute, etc.)

**Session Information:**
- `World/Userlist-User1-platform` - DynamicValueVariable<string> containing user's platform (PC, Mobile, etc.)
- `World/Userlist-User1-jointime` - DynamicValueVariable<string> containing session join time
- `World/Userlist-User1-host` - DynamicValueVariable<bool> whether user is the host
- `World/Userlist-User1-editmode` - DynamicValueVariable<bool> whether user is in edit mode
