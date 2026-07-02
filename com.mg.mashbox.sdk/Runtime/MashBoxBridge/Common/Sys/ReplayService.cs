using System;

namespace MashBoxBridge.Common.Sys
{
    
    public enum ReplayState
    {
        Recording,
        Playback,
        Idle
    }
    
    public interface IReplayService
    {
        ReplayState State { get; }
        Action OnPlaybackStarted { get; set; }
        Action OnPlaybackEnded { get; set; }
        float CurrentPlaybackTime { get; }
        float CurrentRecordTime { get; }
        float MaxRecordTime { get; }
        float PlaybackSpeed { get; }
        bool IsScrubbing { get; }
    }
    
    public static class ReplayService
    {
        public static ReplayState State => _service?.State ?? ReplayState.Recording;
        
        public static IReplayService Service => _service;
        private static IReplayService _service;
        

        public static void SetService(IReplayService service)
        {
            _service = service;
        }

        public static Action OnPlaybackStarted
        {
            get => _service.OnPlaybackStarted;
            set => _service.OnPlaybackStarted = value;
        }

        public static Action OnPlaybackEnded
        {
            get => _service.OnPlaybackEnded;
            set => _service.OnPlaybackEnded = value;
        }
        
        public static float CurrentPlaybackTime => _service == null ? 0.1f : _service.CurrentPlaybackTime;
        public static float CurrentRecordTime =>  _service?.CurrentRecordTime ?? 0.1f;
        public static float MaxRecordTime =>  _service?.MaxRecordTime ?? 30.0f;
        
        public static float PlaybackSpeed =>  _service?.PlaybackSpeed ?? 0.0f;
        public static bool IsScrubbing => _service?.IsScrubbing ?? false;
        
    }
}
