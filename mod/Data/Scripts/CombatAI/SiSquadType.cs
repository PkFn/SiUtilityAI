namespace Si.UtilityAI
{
    public enum SiSquadType
    {
        Infantry,
        Cavalry,
    }

    internal static class SiSquadTypeDefaults
    {
        public static SiSquadType Normalize(SiSquadType squadType)
        {
            return System.Enum.IsDefined(typeof(SiSquadType), (int)squadType)
                ? squadType
                : SiSquadType.Infantry;
        }

        public static SiSquadFormation Formation(SiSquadType squadType)
        {
            switch (Normalize(squadType))
            {
                case SiSquadType.Cavalry:
                    return SiSquadFormation.LongBox;
                case SiSquadType.Infantry:
                default:
                    return SiSquadFormation.Column;
            }
        }

        public static SiSquadType ForMounted(bool isMounted)
        {
            return isMounted ? SiSquadType.Cavalry : SiSquadType.Infantry;
        }

        public static string MapMarkerStyleId(SiSquadType squadType, string relationshipStyle)
        {
            var typeName = Normalize(squadType).ToString().ToLowerInvariant();
            return typeName + "-" + relationshipStyle;
        }
    }
}
