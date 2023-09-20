namespace MySamples
{
    /// <summary>
    /// Centralized configuration for Bot behaviors, distances, and priority weights.
    /// </summary>
    public static class BotConstants
    {
        // Stopping Distances
        public static readonly FP StopDist_Explore = FP._0_75;
        public static readonly FP StopDist_ATM = FP._2;
        public static readonly FP StopDist_DeathZone = FP._1_50;
        public static readonly FP StopDist_Collectible = FP._0_25;
        public static readonly FP StopDist_FlagDelivery = FP._0_25;
        public static readonly FP StopDist_Hill = FP._5;
        public static readonly FP StopDist_Revive = FP._0_50;
        public static readonly FP StopDist_Combat = FP._4;
        
        // Probabilities
        public static readonly FP Chance_Emote = FP._0_10;
        public static readonly FP Chance_StayInBrush = FP._0_10 + FP._0_02;

        // Priorities
        public static readonly FP Priority_Must = FP._10;
        public static readonly FP Priority_Highest = FP._3;
        public static readonly FP Priority_High = FP._2;
        public static readonly FP Priority_Standard = FP._1;
        public static readonly FP Priority_Low = FP._0_50;
        public static readonly FP Priority_None = FP._0;
        
        // Combat
        public static readonly FP Distance_OnScreen = BotHelper.ONSCREEN_DIST;
    }
}