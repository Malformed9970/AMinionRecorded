using System;
using System.IO;
using System.Runtime.InteropServices;
using Hypostasis.Game.Structures;

namespace ARealmRecorded;

public static unsafe class ReplayManager
{
    private static FFXIVReplay* loadedReplay;

    public static void PlaybackUpdate(ContentsReplayModule* contentsReplayModule)
    {
        if (loadedReplay == null) return;

        contentsReplayModule->dataLoadType = 0;
    }

    public static FFXIVReplay.DataSegment* GetReplayDataSegment(ContentsReplayModule* contentsReplayModule)
    {
        if (loadedReplay == null) return null;

        try
        {
            var offset = (uint)contentsReplayModule->overallDataOffset;

            // Spoof native FFXIV 512kb chunk properties dynamically!
            // If we don't spoof this, they math offset = segment - fileStream (yielding up to 200MB) and buffer-overflow!
            var chunkStart = offset & ~0x7FFFFu; // 512kb boundary
            contentsReplayModule->fileStream = (nint)loadedReplay->Data + (nint)chunkStart;
            contentsReplayModule->fileStreamNextWrite = contentsReplayModule->fileStream;
            
            // FFXIV natively uses fileStreamEnd for its bounds-check during chapter jumps.
            // By setting it to the absolute true end of the buffer, we prevent FFXIV from Segfaulting on Native Chapter jumps,
            // while preserving the 512KB spoofed `fileStream` start pointer that external parsers fundamentally require to prevent buffer overflows.
            contentsReplayModule->fileStreamEnd = (nint)loadedReplay->Data + loadedReplay->header.replayLength;
            
            contentsReplayModule->currentFileSection = (int)(offset / 0x80000) + 1;
            contentsReplayModule->dataOffset = offset & 0x7FFFFu;

            return loadedReplay->GetDataSegment(offset);
        }
        catch (Exception e)
        {
            DalamudApi.LogError($"Error getting data segment: {e}");
            return null;
        }
    }

    public static void OnSetChapter(ContentsReplayModule* contentsReplayModule, byte chapter)
    {
        // Pass-through to Native FFXIV Chapter Handler
    }

    public static bool LoadReplay(int slot) => LoadReplay(Path.Combine(Game.replayFolder, Game.GetReplaySlotName(slot)));

    public static bool LoadReplay(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var newReplay = Game.ReadReplay(path);
        if (newReplay == null) return false;

        if (loadedReplay != null)
            Marshal.FreeHGlobal((nint)loadedReplay);

        loadedReplay = newReplay;
        Common.ContentsReplayModule->replayHeader = loadedReplay->header;
        Common.ContentsReplayModule->chapters = loadedReplay->chapters;
        Common.ContentsReplayModule->dataLoadType = 0;

        // Initialize 512KB spoof bounds for compatibility (prevent it from doing Math spanning the entire 200MB file at once)
        Common.ContentsReplayModule->fileStream = (nint)loadedReplay->Data;
        Common.ContentsReplayModule->fileStreamNextWrite = (nint)loadedReplay->Data;
        Common.ContentsReplayModule->fileStreamEnd = (nint)loadedReplay->Data + 0x80000;
        Common.ContentsReplayModule->currentFileSection = 1;

        ARealmRecorded.Config.LastLoadedReplay = path;
        return true;
    }

    public static bool UnloadReplay()
    {
        if (loadedReplay == null) return false;
        Marshal.FreeHGlobal((nint)loadedReplay);
        loadedReplay = null;
        return true;
    }

    public static void Dispose()
    {
        if (loadedReplay == null) return;

        if (Common.ContentsReplayModule->InPlayback)
        {
            Common.ContentsReplayModule->playbackControls |= 8; // Pause
            DalamudApi.PrintError("Plugin was unloaded, playback will be broken if the plugin or replay is not reloaded.");
        }

        Marshal.FreeHGlobal((nint)loadedReplay);
        loadedReplay = null;
    }
}