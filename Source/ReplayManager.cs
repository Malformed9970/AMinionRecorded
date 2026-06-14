using System;
using System.IO;
using System.Runtime.InteropServices;
using Hypostasis.Game.Structures;

namespace ARealmRecorded;

public static unsafe class ReplayManager
{
    private static FFXIVReplay* loadedReplay;
    private static nint dummyBuffer = nint.Zero;

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

            // FFXIV 7.0 bounds checking requires fileStream to stay updated, or large chapter jumps (> 1 hour) cause native crashes.
            var chunkStart = offset & ~0x7FFFFu; // 512kb boundary
            contentsReplayModule->fileStream = (nint)loadedReplay->Data + (nint)chunkStart;
            
            // Critical Fix: FFXIV's native chunk-loader runs on a background thread and attempts to stream the next 512KB into fileStreamNextWrite.
            // We MUST point fileStreamNextWrite to our isolated dummyBuffer, otherwise FFXIV will async-overwrite loadedReplay->Data and corrupt packets after 5 minutes!
            contentsReplayModule->fileStreamNextWrite = dummyBuffer;
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
        if (loadedReplay != null)
        {
            contentsReplayModule->fileStream = (nint)loadedReplay->Data;
            contentsReplayModule->fileStreamNextWrite = dummyBuffer;
            contentsReplayModule->fileStreamEnd = (nint)loadedReplay->Data + loadedReplay->header.replayLength;
            contentsReplayModule->currentFileSection = 1;
        }
    }

    public static bool LoadReplay(int slot) => LoadReplay(Path.Combine(Game.replayFolder, Game.GetReplaySlotName(slot)));

    public static bool LoadReplay(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;

        var newReplay = Game.ReadReplay(path);
        if (newReplay == null) return false;

        if (loadedReplay != null)
            Marshal.FreeHGlobal((nint)loadedReplay);

        if (dummyBuffer == nint.Zero)
            dummyBuffer = Marshal.AllocHGlobal(0x80000);

        loadedReplay = newReplay;
        Common.ContentsReplayModule->replayHeader = loadedReplay->header;
        Common.ContentsReplayModule->chapters = loadedReplay->chapters;
        Common.ContentsReplayModule->dataLoadType = 0;

        Common.ContentsReplayModule->fileStream = (nint)loadedReplay->Data;
        Common.ContentsReplayModule->fileStreamNextWrite = dummyBuffer;
        Common.ContentsReplayModule->fileStreamEnd = (nint)loadedReplay->Data + loadedReplay->header.replayLength;
        Common.ContentsReplayModule->currentFileSection = 1;

        ARealmRecorded.Config.LastLoadedReplay = path;
        return true;
    }

    public static bool UnloadReplay()
    {
        if (loadedReplay == null) return false;
        Marshal.FreeHGlobal((nint)loadedReplay);
        loadedReplay = null;
        
        if (dummyBuffer != nint.Zero)
        {
            Marshal.FreeHGlobal(dummyBuffer);
            dummyBuffer = nint.Zero;
        }
        
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

        if (dummyBuffer != nint.Zero)
        {
            Marshal.FreeHGlobal(dummyBuffer);
            dummyBuffer = nint.Zero;
        }
    }
}