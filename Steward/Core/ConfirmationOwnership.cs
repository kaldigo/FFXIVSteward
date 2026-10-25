namespace Steward.Core;

public static class ConfirmationOwnership
{
    public static bool Matches(ushort owner,ushort parent,ushort host) => owner!=0 && (parent==owner || host==owner);
}
