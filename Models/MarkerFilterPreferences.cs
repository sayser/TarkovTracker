namespace TarkovTracker.Models;

public class MarkerFilterPreferences
{
    public bool Labels { get; set; } = true;
    public bool QuestItems { get; set; }
    public bool QuestObjectives { get; set; }
    public bool PmcExtracts { get; set; } = true;
    public bool ScavExtracts { get; set; } = true;
    public bool SharedExtracts { get; set; } = true;
    public bool Transits { get; set; } = true;
    public bool Hazards { get; set; } = true;
    public bool HazardZones { get; set; } = true;
    public bool Switches { get; set; } = true;
    public bool PmcSpawns { get; set; }
    public bool ScavSpawns { get; set; }
    public bool BossSpawns { get; set; }
    public bool CultistSpawns { get; set; }
    public bool BtrStops { get; set; }
    public bool CustomPins { get; set; }
}
