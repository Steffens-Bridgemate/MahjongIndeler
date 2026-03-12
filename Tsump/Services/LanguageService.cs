namespace Tsump.Services;

public class LanguageService
{
    private string _language = "nl"; // Default to Dutch

    public string Language => _language;
    public bool IsDutch => _language == "nl";
    public bool IsEnglish => _language == "en";

    public event Action? OnLanguageChanged;

    public void SetLanguage(string language)
    {
        if (_language != language)
        {
            _language = language;
            OnLanguageChanged?.Invoke();
        }
    }

    public string Get(string key)
    {
        if (_language == "nl" && Dutch.TryGetValue(key, out var nl))
            return nl;
        if (_language == "en" && English.TryGetValue(key, out var en))
            return en;
        return key;
    }

    // ── Dutch ──────────────────────────────────────────────
    private static readonly Dictionary<string, string> Dutch = new()
    {
        // Nav / Layout
        ["Home"] = "Home",
        ["Members"] = "Leden",
        ["WeeklySession"] = "Zitting",
        ["History"] = "Geschiedenis",
        ["MeetingMatrix"] = "Ontmoetingsmatrix",
        ["ClubManager"] = "Clubbeheer",
        ["MahjongclubTsumo"] = "Mahjongclub Tsumo!",

        // Home page
        ["WelcomeTo"] = "Welkom bij Tsumo!",
        ["ManageMembers"] = "Leden beheren",
        ["MembersDesc"] = "Leden toevoegen, bewerken en beheren.",
        ["StartSession"] = "Zitting aanmaken",
        ["WeeklySessionDesc"] = "Aanwezigheid bijhouden en tafels indelen.",
        ["ViewHistory"] = "Geschiedenis bekijken",
        ["HistoryDesc"] = "Eerdere zittingen en spelerstatistieken bekijken.",
        ["ViewMatrix"] = "Matrix bekijken",
        ["MeetingMatrixDesc"] = "Bekijk hoe vaak spelers tegen elkaar gespeeld hebben.",

        // Members page
        ["ClubMembers"] = "Clubleden",
        ["AddMember"] = "+ Lid toevoegen",
        ["EditMember"] = "Lid bewerken",
        ["AddNewMember"] = "Nieuw lid toevoegen",
        ["Name"] = "Naam",
        ["Email"] = "E-mail",
        ["Phone"] = "Telefoon",
        ["Active"] = "Actief",
        ["Inactive"] = "Inactief",
        ["Joined"] = "Lid sinds",
        ["Status"] = "Status",
        ["Actions"] = "Acties",
        ["Edit"] = "Bewerken",
        ["Remove"] = "Verwijderen",
        ["Save"] = "Opslaan",
        ["Cancel"] = "Annuleren",
        ["NoMembersYet"] = "Nog geen leden. Voeg je eerste clublid toe!",
        ["Loading"] = "Laden...",
        ["Total"] = "Totaal",
        ["members"] = "leden",
        ["active"] = "actief",

        // Weekly Session page
        ["SessionDate"] = "Zittingsdatum",
        ["StartTime"] = "Starttijd",
        ["SessionsOnDate"] = "Zittingen op deze datum",
        ["NewSession"] = "Nieuwe zitting",
        ["NoTime"] = "Geen tijd",
        ["SwapPlayers"] = "Wisselen",
        ["Selected"] = "Geselecteerd",
        ["ClearSelection"] = "Selectie wissen",
        ["CopyToClipboard"] = "Kopiëren naar klembord",
        ["Copied"] = "Gekopieerd!",
        ["DeleteSessionConfirm"] = "Weet je zeker dat je deze zitting wilt verwijderen?",
        ["YesDelete"] = "Ja, verwijderen",
        ["Attendance"] = "Aanwezigheid",
        ["present"] = "aanwezig",
        ["of"] = "van",
        ["AllPresent"] = "Allen aanwezig",
        ["AllAbsent"] = "Allen afwezig",
        ["SearchMembers"] = "Leden zoeken...",
        ["GenerateTableAssignments"] = "Tafelindeling maken",
        ["SaveSession"] = "Zitting opslaan",
        ["NeedAtLeast3"] = "Minimaal 3 aanwezige spelers nodig.",
        ["TableAssignments"] = "Tafelindeling",
        ["Table"] = "Tafel",
        ["players"] = "spelers",
        ["Unknown"] = "Onbekend",
        ["UnassignedPlayers"] = "speler(s) kon(den) niet worden ingedeeld (niet genoeg voor een groep van 3).",
        ["SessionSaved"] = "Zitting succesvol opgeslagen!",
        ["PastSessionReadOnly"] = "Dit is een afgeronde zitting. Alleen-lezen.",
        ["RegenerateConfirm"] = "Deze zitting heeft al een tafelindeling. Opnieuw genereren?",
        ["YesRegenerate"] = "Ja, opnieuw",
        ["AddMembersFirst"] = "Nog geen leden geregistreerd.",
        ["AddMembersLink"] = "Voeg eerst leden toe",
        ["NoMembersRegistered"] = "Nog geen leden geregistreerd.",

        // History page
        ["SessionHistory"] = "Geschiedenis",
        ["NoSessionsYet"] = "Nog geen zzittingen vastgelegd.",
        ["CreateFirstSession"] = "Maak je eerste zitting",
        ["PresentPlayers"] = "Aanwezige spelers",
        ["AbsentPlayers"] = "Afwezige spelers",
        ["DeleteSession"] = "Zitting verwijderen",
        ["PlayerStatistics"] = "Spelerstatistieken",
        ["Player"] = "Speler",
        ["SessionsAttended"] = "Zittingen aanwezig",
        ["TimesAt3Table"] = "Keer aan 3-spelertafel",
        ["TimesAt4Table"] = "Keer aan 4-spelertafel",
        ["table"] = "tafel",
        ["tables"] = "tafels",

        // Meeting Matrix page
        ["MeetingMatrixTitle"] = "Ontmoetingsmatrix",
        ["MeetingMatrixSubtitle"] = "Hoe vaak elk paar spelers aan dezelfde tafel heeft gezeten.",
        ["NoSessionData"] = "Nog geen zittingsgegevens.",
        ["Legend"] = "Legenda",
        ["NeverMet"] = "0 = nog niet ontmoet",
        ["Meetings1_2"] = "1-2 ontmoetingen",
        ["Meetings3_5"] = "3-5 ontmoetingen",
        ["Meetings6Plus"] = "6+ ontmoetingen",
        ["Summary"] = "Samenvatting",
        ["TotalPairs"] = "Totaal spelerparen",
        ["PairsMet"] = "Paren die elkaar ontmoet hebben",
        ["PairsNeverMet"] = "Paren die elkaar nog niet ontmoet hebben",
        ["AvgMeetings"] = "Gemiddeld aantal ontmoetingen per paar (van degenen die ontmoet hebben)",
        ["MinMeetings"] = "Min ontmoetingen",
        ["MaxMeetings"] = "Max ontmoetingen",

        // Data Management page
        ["DataManagement"] = "Gegevensbeheer",
        ["ExportData"] = "Gegevens exporteren",
        ["ExportDescription"] = "Exporteer alle leden en zittingen naar een JSON-bestand.",
        ["ExportToFile"] = "Exporteren naar bestand",
        ["ExportSuccess"] = "Gegevens succesvol geëxporteerd!",
        ["ImportData"] = "Gegevens importeren",
        ["ImportDescription"] = "Importeer leden en zittingen uit een eerder geëxporteerd JSON-bestand. Dit vervangt alle huidige gegevens.",
        ["ImportFromFile"] = "Importeren uit bestand",
        ["ImportConfirm"] = "Let op: importeren vervangt alle huidige gegevens (leden en zittingen). Weet je het zeker?",
        ["YesImport"] = "Ja, importeren",
        ["ImportSuccess"] = "Gegevens succesvol geïmporteerd!",
        ["InvalidFileFormat"] = "Ongeldig bestandsformaat. Gebruik een eerder geëxporteerd Tsumo!-bestand.",
        ["FileTooLarge"] = "Bestand is te groot (max 10 MB).",
        ["CurrentDataSummary"] = "Huidige gegevens",
        ["Sessions"] = "Zittingen",
    };

    // ── English ────────────────────────────────────────────
    private static readonly Dictionary<string, string> English = new()
    {
        // Nav / Layout
        ["Home"] = "Home",
        ["Members"] = "Members",
        ["WeeklySession"] = "Weekly Session",
        ["History"] = "History",
        ["MeetingMatrix"] = "Meeting Matrix",
        ["ClubManager"] = "Club Manager",
        ["MahjongclubTsumo"] = "Mahjongclub Tsumo",

        // Home page
        ["WelcomeTo"] = "Welcome to Tsumo!",
        ["ManageMembers"] = "Manage Members",
        ["MembersDesc"] = "Add, edit, and manage club members.",
        ["StartSession"] = "Start Session",
        ["WeeklySessionDesc"] = "Track attendance and assign tables.",
        ["ViewHistory"] = "View History",
        ["HistoryDesc"] = "View past sessions and player statistics.",
        ["ViewMatrix"] = "View Matrix",
        ["MeetingMatrixDesc"] = "See how often each pair of players has met.",

        // Members page
        ["ClubMembers"] = "Club Members",
        ["AddMember"] = "+ Add Member",
        ["EditMember"] = "Edit Member",
        ["AddNewMember"] = "Add New Member",
        ["Name"] = "Name",
        ["Email"] = "Email",
        ["Phone"] = "Phone",
        ["Active"] = "Active",
        ["Inactive"] = "Inactive",
        ["Joined"] = "Joined",
        ["Status"] = "Status",
        ["Actions"] = "Actions",
        ["Edit"] = "Edit",
        ["Remove"] = "Remove",
        ["Save"] = "Save",
        ["Cancel"] = "Cancel",
        ["NoMembersYet"] = "No members yet. Add your first club member!",
        ["Loading"] = "Loading...",
        ["Total"] = "Total",
        ["members"] = "members",
        ["active"] = "active",

        // Weekly Session page
        ["SessionDate"] = "Session Date",
        ["StartTime"] = "Start Time",
        ["SessionsOnDate"] = "Sessions on this date",
        ["NewSession"] = "New Session",
        ["NoTime"] = "No time",
        ["SwapPlayers"] = "Swap",
        ["Selected"] = "Selected",
        ["ClearSelection"] = "Clear selection",
        ["CopyToClipboard"] = "Copy to clipboard",
        ["Copied"] = "Copied!",
        ["DeleteSessionConfirm"] = "Are you sure you want to delete this session?",
        ["YesDelete"] = "Yes, delete",
        ["Attendance"] = "Attendance",
        ["present"] = "present",
        ["of"] = "of",
        ["AllPresent"] = "All Present",
        ["AllAbsent"] = "All Absent",
        ["SearchMembers"] = "Search members...",
        ["GenerateTableAssignments"] = "Generate Table Assignments",
        ["SaveSession"] = "Save Session",
        ["NeedAtLeast3"] = "Need at least 3 present players.",
        ["TableAssignments"] = "Table Assignments",
        ["Table"] = "Table",
        ["players"] = "players",
        ["Unknown"] = "Unknown",
        ["UnassignedPlayers"] = "player(s) could not be assigned to a table (not enough for a group of 3).",
        ["SessionSaved"] = "Session saved successfully!",
        ["PastSessionReadOnly"] = "This is a past session that has been finalized. It is read-only.",
        ["RegenerateConfirm"] = "This session already has table assignments. Regenerate?",
        ["YesRegenerate"] = "Yes, regenerate",
        ["AddMembersFirst"] = "No members registered yet.",
        ["AddMembersLink"] = "Add members first",
        ["NoMembersRegistered"] = "No members registered yet.",

        // History page
        ["SessionHistory"] = "Session History",
        ["NoSessionsYet"] = "No sessions recorded yet.",
        ["CreateFirstSession"] = "Create your first session",
        ["PresentPlayers"] = "Present Players",
        ["AbsentPlayers"] = "Absent Players",
        ["DeleteSession"] = "Delete Session",
        ["PlayerStatistics"] = "Player Statistics",
        ["Player"] = "Player",
        ["SessionsAttended"] = "Sessions Attended",
        ["TimesAt3Table"] = "Times at 3-Player Table",
        ["TimesAt4Table"] = "Times at 4-Player Table",
        ["table"] = "table",
        ["tables"] = "tables",

        // Meeting Matrix page
        ["MeetingMatrixTitle"] = "Meeting Matrix",
        ["MeetingMatrixSubtitle"] = "How many times each pair of players has been at the same table.",
        ["NoSessionData"] = "No session data yet.",
        ["Legend"] = "Legend",
        ["NeverMet"] = "0 = never met",
        ["Meetings1_2"] = "1-2 meetings",
        ["Meetings3_5"] = "3-5 meetings",
        ["Meetings6Plus"] = "6+ meetings",
        ["Summary"] = "Summary",
        ["TotalPairs"] = "Total player pairs",
        ["PairsMet"] = "Pairs that have met",
        ["PairsNeverMet"] = "Pairs that have never met",
        ["AvgMeetings"] = "Average meetings per pair (of those who met)",
        ["MinMeetings"] = "Min meetings",
        ["MaxMeetings"] = "Max meetings",

        // Data Management page
        ["DataManagement"] = "Data Management",
        ["ExportData"] = "Export Data",
        ["ExportDescription"] = "Export all members and sessions to a JSON file.",
        ["ExportToFile"] = "Export to file",
        ["ExportSuccess"] = "Data exported successfully!",
        ["ImportData"] = "Import Data",
        ["ImportDescription"] = "Import members and sessions from a previously exported JSON file. This replaces all current data.",
        ["ImportFromFile"] = "Import from file",
        ["ImportConfirm"] = "Warning: importing will replace all current data (members and sessions). Are you sure?",
        ["YesImport"] = "Yes, import",
        ["ImportSuccess"] = "Data imported successfully!",
        ["InvalidFileFormat"] = "Invalid file format. Please use a previously exported Tsumo! file.",
        ["FileTooLarge"] = "File is too large (max 10 MB).",
        ["CurrentDataSummary"] = "Current data",
        ["Sessions"] = "Sessions",
    };
}
