using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Hypostasis.Game.Structures;
using Dalamud.Bindings.ImGui;

namespace ARealmRecorded;

public static unsafe class PlaybackControlsUI
{
    public static readonly float[] presetSpeeds = [ 0.5f, 1, 2, 5, 10, 20 ];

    private static bool loadingPlayback = false;
    private static bool loadedPlayback = true;

    private static bool shouldPlaybackControlHide = false;
    private static bool showReplaySettings = false;
    private static bool showDebug = false;

    private static float lastSeek = 0;
    private static bool showUnstuckButton = false;
    private static readonly Stopwatch unstuckTimer = new();

    public static void Draw()
    {
        if (DalamudApi.GameGui.GameUiHidden || DalamudApi.Condition[ConditionFlag.WatchingCutscene]) return;

        if (!Common.ContentsReplayModule->InPlayback)
        {
            if (!loadingPlayback && !loadedPlayback) return;

            loadingPlayback = false;
            loadedPlayback = false;
            Game.fixCountdownPatch.Disable();
            return;
        }

        if (DalamudApi.GameGui.GetAddonByName("TalkSubtitle") != nint.Zero) return; // Hide during cutscenes

        if (Common.ContentsReplayModule->seek != lastSeek || Common.ContentsReplayModule->IsPaused)
        {
            lastSeek = Common.ContentsReplayModule->seek;
            unstuckTimer.Restart();
            showUnstuckButton = false;
        }
        else if (unstuckTimer.ElapsedMilliseconds > 3_000)
        {
            showUnstuckButton = true;
            loadedPlayback = true;
        }

        if (!loadedPlayback)
        {
            if (Common.ContentsReplayModule->u0x720 != 0)
            {
                loadingPlayback = true;
            }
            else if (loadingPlayback && Common.ContentsReplayModule->u0x720 == 0)
            {
                loadedPlayback = true;
                if (!ARealmRecorded.Config.EnableWaymarks)
                    Game.ToggleWaymarks();
                Game.fixCountdownPatch.Enable();
            }
            return;
        }

        var addon = (AtkUnitBase*)DalamudApi.GameGui.GetAddonByName("ContentsReplayPlayer").Address;
        if (addon == null || (ARealmRecorded.Config.EnablePlaybackControlHiding && !addon->IsVisible && !showUnstuckButton))
        {
            shouldPlaybackControlHide = true;
            return;
        }

        using var _ = ImGuiEx.StyleVarBlock.Begin(ImGuiStyleVar.Alpha, ARealmRecorded.Config.EnablePlaybackControlHiding && shouldPlaybackControlHide && !showUnstuckButton ? 0.001f : 1);
        var addonW = addon->RootNode->GetWidth() * addon->Scale;
        var addonPadding = addon->Scale * 8;
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGui.SetNextWindowPos(new Vector2(addon->X + addonPadding, addon->Y + addonPadding) + ImGuiHelpers.MainViewport.Pos, ImGuiCond.Always, Vector2.UnitY);
        ImGui.SetNextWindowSize(new Vector2(addonW - addonPadding * 2, 0));
        ImGui.Begin("Expanded Playback", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);

        if (showReplaySettings && !Common.ContentsReplayModule->IsLoadingChapter)
        {
            DrawReplaySettings();
            ImGui.Separator();
        }
        else if (showDebug)
        {
            DrawDebug();
            ImGui.Separator();
        }

        //if (ImGuiEx.FontButton(FontAwesomeIcon.List.ToIconString(), UiBuilder.IconFont))
        //    ReplayListUI.DisplayDetachedReplayList ^= true;
        //ImGuiEx.SetItemTooltip("Display replay list.");

        //ImGui.SameLine();
        if (ImGuiEx.FontButton(FontAwesomeIcon.Users.ToIconString(), UiBuilder.IconFont))
            DalamudApi.Framework.RunOnTick(() => Framework.Instance()->GetUIModule()->EnterGPose());
        ImGuiEx.SetItemTooltip("Enters group pose.");

        ImGui.SameLine();
        if (ImGuiEx.FontButton(FontAwesomeIcon.Video.ToIconString(), UiBuilder.IconFont))
        {
            var focusId = DalamudApi.TargetManager.FocusTarget is { } focus ? focus.GameObjectId : 0xE0000000;
            DalamudApi.Framework.RunOnTick(() => Framework.Instance()->GetUIModule()->EnterIdleCam(0, focusId));
        }
        ImGuiEx.SetItemTooltip("Enters idle camera on the current focus target.");

        ImGui.SameLine();
        var v = Game.IsWaymarkVisible;
        if (ImGuiEx.FontButton(v ? FontAwesomeIcon.ToggleOn.ToIconString() : FontAwesomeIcon.ToggleOff.ToIconString(), UiBuilder.IconFont))
        {
            DalamudApi.Framework.RunOnTick(() => Game.ToggleWaymarks());
            ARealmRecorded.Config.EnableWaymarks ^= true;
            ARealmRecorded.Config.Save();
        }
        ImGuiEx.SetItemTooltip(v ? "Hide waymarks." : "Show waymarks.");

        using (ImGuiEx.FontBlock.Begin(UiBuilder.IconFont))
        {
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.Cog.ToIconString()))
                showReplaySettings ^= true;

            ImGui.SameLine();
            ImGui.Button(FontAwesomeIcon.Skull.ToIconString());
            if (ImGui.BeginPopupContextItem(ImU8String.Empty, ImGuiPopupFlags.MouseButtonLeft))
            {
                if (ImGui.Selectable(FontAwesomeIcon.DoorOpen.ToIconString()))
                    DalamudApi.Framework.RunOnTick(() => Common.ContentsReplayModule->overallDataOffset = long.MaxValue);
                ImGui.EndPopup();
            }

#if DEBUG
            ImGui.SameLine();
            if (ImGui.Button(FontAwesomeIcon.ExclamationTriangle.ToIconString()))
                showDebug ^= true;
#endif
        }

        ImGui.SameLine();
        if (ImGuiEx.FontButton(FontAwesomeIcon.StepBackward.ToIconString(), UiBuilder.IconFont))
        {
            var chapter = Common.ContentsReplayModule->GetPreviousStartChapter();
            Game.JumpToChapter(chapter);
        }
        ImGuiEx.SetItemTooltip("Jump to previous pull");

        if (showUnstuckButton)
        {
            ImGui.SameLine();
            var segment = Game.GetReplayDataSegmentDetour(Common.ContentsReplayModule);

            using (ImGuiEx.StyleColorBlock.Begin(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.TabActive)))
            {
                if (ImGui.Button("UNSTUCK") && segment != null)
                {
                    var len = segment->Length;
                    DalamudApi.Framework.RunOnTick(() => Common.ContentsReplayModule->overallDataOffset += len);
                }
            }
        }


        using (ImGuiEx.FontBlock.Begin(UiBuilder.IconFont))
        {
            var buttonSize = ImGui.CalcTextSize(FontAwesomeIcon.EyeSlash.ToIconString()) + ImGui.GetStyle().FramePadding * 2;
            ImGui.SameLine(ImGui.GetContentRegionMax().X - buttonSize.X);
            if (ImGui.Button(ARealmRecorded.Config.EnablePlaybackControlHiding ? FontAwesomeIcon.EyeSlash.ToIconString() : FontAwesomeIcon.Eye.ToIconString(), buttonSize))
            {
                ARealmRecorded.Config.EnablePlaybackControlHiding ^= true;
                ARealmRecorded.Config.Save();
            }
        }
        ImGuiEx.SetItemTooltip("Hides the menu under certain circumstances.");

        var sliderWidth = ImGui.GetContentRegionAvail().X;

        using (ImGuiEx.ItemWidthBlock.Begin(sliderWidth))
        {
            var speed = Common.ContentsReplayModule->speed;
            if (ImGui.SliderFloat("##Speed", ref speed, 0.05f, 10.0f, "%.2fx", ImGuiSliderFlags.AlwaysClamp))
            {
                var s = speed;
                DalamudApi.Framework.RunOnTick(() => Common.ContentsReplayModule->speed = s);
            }
        }

        for (int i = 0; i < presetSpeeds.Length; i++)
        {
            if (i != 0)
                ImGui.SameLine();

            var s = presetSpeeds[i];
            if (ImGui.Button($"{s}x"))
            {
                // check what it is natively before toggling
                var currentNativeSpeed = Common.ContentsReplayModule->speed;
                DalamudApi.Framework.RunOnTick(() => Common.ContentsReplayModule->speed = s == currentNativeSpeed ? 1 : s);
            }
        }

        var customSpeed = ARealmRecorded.Config.CustomSpeedPreset;
        ImGui.SameLine();
        ImGui.Dummy(Vector2.Zero);
        ImGui.SameLine();
        if (ImGui.Button($"{customSpeed}x"))
        {
            var currentNativeSpeed = Common.ContentsReplayModule->speed;
            DalamudApi.Framework.RunOnTick(() => Common.ContentsReplayModule->speed = customSpeed == currentNativeSpeed ? 1 : customSpeed);
        }

        shouldPlaybackControlHide = !ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.RectOnly);

        ImGui.End();
    }

    private static void DrawReplaySettings()
    {
        var save = false;

        if (ImGui.Checkbox("Hide Own Name (Requires Replay Restart)", ref ARealmRecorded.Config.EnableHideOwnName))
        {
            Game.replaceLocalPlayerNamePatch.Toggle();
            save = true;
        }

        save |= ImGui.SliderFloat("Speed Preset", ref ARealmRecorded.Config.CustomSpeedPreset, 0.05f, 60, "%.2fx", ImGuiSliderFlags.AlwaysClamp);

        save |= ImGui.Checkbox("Auto-Pause after Chapter Select", ref ARealmRecorded.Config.AutoPauseAfterChapterSelect);

        if (save)
            ARealmRecorded.Config.Save();
}

    [Conditional("DEBUG")]
    private static void DrawDebug()
    {
        var segment = Game.GetReplayDataSegmentDetour(Common.ContentsReplayModule);
        if (segment == null) return;

        ImGui.TextUnformatted($"Offset: {Common.ContentsReplayModule->overallDataOffset + sizeof(FFXIVReplay.Header) + sizeof(FFXIVReplay.ChapterArray):X}");
        ImGui.TextUnformatted($"Opcode: {segment->opcode:X}");
        ImGui.TextUnformatted($"Data Length: {segment->dataLength}");
        ImGui.TextUnformatted($"Time: {segment->ms / 1000f}");
        ImGui.TextUnformatted($"Object ID: {segment->objectID:X}");
    }
}