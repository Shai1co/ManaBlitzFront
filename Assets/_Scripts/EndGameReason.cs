namespace ManaGambit
{
    /// <summary>
    /// End game reasons matching server implementation.
    /// Must stay in sync with server's EndGameReason enum.
    /// </summary>
    public enum EndGameReason
    {
        /// <summary>
        /// Game ended due to elimination (a brawler died or a player left)
        /// </summary>
        Elimination = 1,
        
        /// <summary>
        /// Game ended due to timeout (time limit reached / draw)
        /// </summary>
        Timeout = 2
    }
    
    /// <summary>
    /// Helper methods for EndGameReason
    /// </summary>
    public static class EndGameReasonExtensions
    {
        /// <summary>
        /// Converts an integer reason from server to EndGameReason enum
        /// </summary>
        /// <param name="reason">Integer reason from server</param>
        /// <returns>EndGameReason enum value, or null if invalid</returns>
        public static EndGameReason? FromInt(int reason)
        {
            switch (reason)
            {
                case 1: return EndGameReason.Elimination;
                case 2: return EndGameReason.Timeout;
                default: return null;
            }
        }
        
        /// <summary>
        /// Gets a human-readable description of the end game reason
        /// </summary>
        /// <param name="reason">The end game reason</param>
        /// <returns>Human-readable description</returns>
        public static string GetDescription(this EndGameReason reason)
        {
            switch (reason)
            {
                case EndGameReason.Elimination:
                    return "Victory by Elimination";
                case EndGameReason.Timeout:
                    return "Match Timed Out";
                default:
                    return "Unknown Reason";
            }
        }
        
        /// <summary>
        /// Gets a brief description suitable for UI display
        /// </summary>
        /// <param name="reason">The end game reason</param>
        /// <returns>Brief UI-friendly description</returns>
        public static string GetUIDescription(this EndGameReason reason)
        {
            switch (reason)
            {
                case EndGameReason.Elimination:
                    return "Elimination";
                case EndGameReason.Timeout:
                    return "Time's Up!";
                default:
                    return "Game Over";
            }
        }
    }
}
