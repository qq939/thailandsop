namespace VideoInferenceDemo;

public readonly record struct FsmFrameFeatures
{
    public int? CenterScoreQ1000 { get; init; }
    public int? ScoreId0Q1000 { get; init; }
    public int? ScoreId1Q1000 { get; init; }
    public int DistId0ToId2Q1000 { get; init; }
    public int DistId1ToId2Q1000 { get; init; }
    public int? AreaId0Px { get; init; }
    public int? AreaId1Px { get; init; }
    public int? AreaId2Px { get; init; }
}
