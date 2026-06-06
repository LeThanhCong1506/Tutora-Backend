namespace MV.DomainLayer.Constants;

public static class TutorSearchBudgetRange
{
    public const string AllValue = "all";
    public const string Under50 = "under_50";
    public const string Range50_100 = "50_100";
    public const string Range100_200 = "100_200";
    public const string Range200_500 = "200_500";
    public const string Over500 = "over_500";

    public static readonly string[] ValidValues = new[] 
    { 
        AllValue, Under50, Range50_100, Range100_200, Range200_500, Over500 
    };
}
