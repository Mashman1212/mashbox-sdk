

namespace MashBoxBridge.Common.Sys
{
    public interface ILeaderboardService
    {
        public void SubmitScore(string boardID, float score);
        public void SelectLeaderboard(string boardID);
    }

    public static class LeaderboardService
    {
        private static ILeaderboardService _service;
            
        public static void SetService(ILeaderboardService service)
        {
            _service = service;
        }
        public static void SubmitScore(string boardID, float score)
        {
            if (_service != null)
            {
                _service.SubmitScore(boardID,score);
            }
        }

        public static void SelectLeaderboard(string boardID)
        {
            if (_service != null)
            {
                _service.SelectLeaderboard(boardID);
            }
        }
    }
}
