namespace ManaGambit
{
    /// <summary>
    /// Game types that align with server's BotTypeResolver logic
    /// </summary>
    public enum GameType
    {
        /// <summary>
        /// Play Online - matchmaking queue with potential bot fallback
        /// </summary>
        PlayOnline,
        
        /// <summary>
        /// Vs Bot - explicit bot opponent with difficulty selection
        /// </summary>
        VsBot,
        
        /// <summary>
        /// Pure PvP - human vs human only
        /// </summary>
        PvP
    }
    
    /// <summary>
    /// Difficulty levels for bot opponents
    /// </summary>
    public enum BotDifficulty
    {
        Easy,
        Medium,
        Hard
    }
    
    /// <summary>
    /// Game launch configuration that maps to server expectations
    /// </summary>
    [System.Serializable]
    public class GameLaunchConfig
    {
        public GameType gameType;
        public BotDifficulty? botDifficulty; // Only used for VsBot game type
        
        public GameLaunchConfig(GameType gameType, BotDifficulty? botDifficulty = null)
        {
            this.gameType = gameType;
            this.botDifficulty = botDifficulty;
        }
        
        /// <summary>
        /// Converts this config to the server mode string expected by the queue API
        /// </summary>
        /// <returns>Mode string for server</returns>
        public string ToServerMode()
        {
            switch (gameType)
            {
                case GameType.PlayOnline:
                    return "arena"; // Server treats this as queue mode, may fallback to bot
                case GameType.VsBot:
                    return "bot"; // Server treats this as explicit bot mode
                case GameType.PvP:
                    return "pvp"; // Server treats this as human-only mode
                default:
                    return "arena"; // Default fallback
            }
        }
        
        /// <summary>
        /// Gets the difficulty string for server if applicable
        /// </summary>
        /// <returns>Difficulty string or null</returns>
        public string GetDifficultyString()
        {
            if (gameType != GameType.VsBot || !botDifficulty.HasValue)
                return null;
                
            switch (botDifficulty.Value)
            {
                case BotDifficulty.Easy:
                    return "easy";
                case BotDifficulty.Medium:
                    return "medium";
                case BotDifficulty.Hard:
                    return "hard";
                default:
                    return "easy";
            }
        }
        
        /// <summary>
        /// Creates a config for Play Online mode
        /// </summary>
        public static GameLaunchConfig PlayOnline()
        {
            return new GameLaunchConfig(GameType.PlayOnline);
        }
        
        /// <summary>
        /// Creates a config for Vs Bot mode with specified difficulty
        /// </summary>
        public static GameLaunchConfig VsBot(BotDifficulty difficulty)
        {
            return new GameLaunchConfig(GameType.VsBot, difficulty);
        }
        
        /// <summary>
        /// Creates a config for Pure PvP mode
        /// </summary>
        public static GameLaunchConfig PvP()
        {
            return new GameLaunchConfig(GameType.PvP);
        }
        
        public override string ToString()
        {
            if (gameType == GameType.VsBot && botDifficulty.HasValue)
            {
                return $"{gameType} ({botDifficulty.Value})";
            }
            return gameType.ToString();
        }
    }
    
    /// <summary>
    /// Helper extensions for game types
    /// </summary>
    public static class GameTypeExtensions
    {
        /// <summary>
        /// Gets a user-friendly display name for the game type
        /// </summary>
        public static string GetDisplayName(this GameType gameType)
        {
            switch (gameType)
            {
                case GameType.PlayOnline:
                    return "Play Online";
                case GameType.VsBot:
                    return "Vs Bot";
                case GameType.PvP:
                    return "Player vs Player";
                default:
                    return gameType.ToString();
            }
        }
        
        /// <summary>
        /// Gets a user-friendly display name for bot difficulty
        /// </summary>
        public static string GetDisplayName(this BotDifficulty difficulty)
        {
            switch (difficulty)
            {
                case BotDifficulty.Easy:
                    return "Easy";
                case BotDifficulty.Medium:
                    return "Medium";
                case BotDifficulty.Hard:
                    return "Hard";
                default:
                    return difficulty.ToString();
            }
        }
        
        /// <summary>
        /// Gets a description for the game type
        /// </summary>
        public static string GetDescription(this GameType gameType)
        {
            switch (gameType)
            {
                case GameType.PlayOnline:
                    return "Find an online match. May play against a bot if no human opponent is available.";
                case GameType.VsBot:
                    return "Play against an AI opponent with selectable difficulty.";
                case GameType.PvP:
                    return "Play against another human player only.";
                default:
                    return "";
            }
        }
    }
}
