namespace Steward.Core;

public static class WorkflowFilter
{
    // AutoRetainer.Workflow.Mask v1: ventures 1, gil 2, entrust 4, market 8,
    // collection 16, dresser cleanup 32, disposal 64, other plugins 128.
    public static FeatureSettings Apply(FeatureSettings features,int mask)=>new()
    {
        NewListings=features.NewListings&&(mask&8)!=0,
        Repricing=features.Repricing&&(mask&8)!=0,
        ListingReturns=features.ListingReturns&&(mask&8)!=0,
        Rotation=features.Rotation&&(mask&8)!=0,
        DresserCollection=features.DresserCollection&&(mask&16)!=0,
        ArmoireStorage=features.ArmoireStorage&&(mask&16)!=0,
        OverflowStorage=features.OverflowStorage&&(mask&4)!=0,
        Disposal=features.Disposal&&(mask&64)!=0,
    };
}
