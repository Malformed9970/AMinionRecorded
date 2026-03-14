using Dalamud.Configuration;

namespace ARealmRecorded;

public class Configuration : PluginConfiguration, IPluginConfiguration
{
    public int Version { get; set; }
    public string LastLoadedReplay;
    public bool EnableRecordingIcon = false;
    public int MaxAutoRenamedReplays = 30;
    public int MaxDeletedReplays = 10;
    public bool EnablePlaybackControlHiding = false;
    public bool EnableHideOwnName = false;
    public float CustomSpeedPreset = 30;
    public bool AutoPauseAfterChapterSelect = true;
    public bool EnableWaymarks = true;
}