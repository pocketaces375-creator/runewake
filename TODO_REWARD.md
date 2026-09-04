Changes needed for TASK-REWARD-SCREEN-1 in DuelScene.cs:

1. Add _rewardCounters list, _countersAnimated flag, AnimatedRewardCounter struct after line 3534
2. Add encounter portrait (TextureRect) to BuildGameOverOverlay around line 4050
3. Replace static MakeRewardRow calls with AnimatedCountUpRow calls (lines 4144-4169)
4. Add defeat "what was lost" section (forfeited rewards) after line 4193's else branch
5. Add StartAnimatedCounters method
6. Wire animation start after overlay is built (after line 4269)
7. Update ShowGameOverOverlay to use BuildGameOverOverlay's defeat path (remove old overlay)