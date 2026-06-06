namespace MV.DomainLayer.Constants;

public static class TutorSearchCategory
{
    public const string AllValue = "all";
    public const string Science = "science";
    public const string Language = "language";
    public const string Art = "art";
    public const string ItTech = "it_tech";
    public const string Math = "math";
    public const string Physics = "physics";
    public const string Chemistry = "chemistry";
    public const string English = "english";

    public static readonly string[] ValidValues = new[] 
    { 
        AllValue, Science, Language, Art, ItTech, Math, Physics, Chemistry, English 
    };
}
