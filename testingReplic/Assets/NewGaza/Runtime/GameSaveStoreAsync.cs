using System;
using NewGaza.Core;

namespace NewGaza
{
    public static partial class GameSaveStore
    {
        private sealed class WriteRequest { internal GameState state; internal string path; }
        private static readonly BackgroundSaveQueue<WriteRequest> background =
            new BackgroundSaveQueue<WriteRequest>(request => SaveAt(request.state, request.path));

        public static void Enqueue(GameState liveState)
        {
            // Unity path/camera/fleet APIs stay on the main thread. Unity documents ToJson
            // as thread-safe for plain objects that are not modified during serialization.
            background.Enqueue(new WriteRequest { state = GameStateSnapshot.Capture(liveState), path = SavePath });
        }
        public static void FlushPending() => background.Flush();
        public static Exception TakeSaveFailure() => background.TakeFailure();
    }
}