namespace Steward.Core;

public static class AutoDutyTravel
{
    public static bool Owned(Func<string,bool?> read)
    {
        foreach(var name in new[]{"AutoDuty.IsLooping","AutoDuty.IsNavigating","AutoDuty.IsStopped"})
        {
            try
            {
                var value=read(name);
                if(value.HasValue&&(name=="AutoDuty.IsStopped"?!value.Value:value.Value))return true;
            }
            catch{return true;}
        }
        return false;
    }
}
