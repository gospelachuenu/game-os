namespace CecController;

/// <summary>
/// The exact cec-client argument strings plan.md §8.2/§8.3 specifies. Centralized here
/// so the command text is defined once and referenced by name everywhere else.
/// </summary>
public static class CecCommands
{
    public const string ActiveSource = "-as";
    public const string PowerOn = "-p on";
    public const string PowerStandby = "-p standby";
}
