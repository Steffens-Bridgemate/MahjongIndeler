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

    public string FormatDate(DateTime date)
    {
        var day = Get(date.DayOfWeek.ToString());
        var month = Get($"Month{date.Month}");
        return $"{day} {date.Day} {month} {date.Year}";
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
        ["WorkflowHomeDesc"] = "Volg het stappenplan om een zitting voor te bereiden en af te ronden.",
        ["StartWorkflow"] = "Start stappenplan",

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
        ["ExtraThreePlayerTables"] = "Extra 3-spelertafels",
        ["ExtraThreePlayerTablesDesc"] = "Aantal 3-spelertafels buiten de zittingsgeschiedenis om.",

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

        // Settings page
        ["Settings"] = "Instellingen",
        ["PlayingDays"] = "Speeldagen",
        ["PlayingDaysDesc"] = "Selecteer de dagen waarop de club speelt en stel de starttijden in.",
        ["AddTime"] = "Tijd toevoegen",
        ["SettingsSaved"] = "Instellingen opgeslagen!",

        // Workflow page
        ["Workflow"] = "Stappenplan",
        ["WorkflowDesc"] = "Volg de onderstaande stappen om een zitting voor te bereiden en af te ronden.",
        ["WfStep1Title"] = "Stap 1: Clubgegevens kopiëren",
        ["WfStep1Desc"] = "Kopieer de clubgegevens uit de map \"Competitiecommissie Tsumo 2025\" naar een lokale map.",
        ["WfStep2Title"] = "Stap 2: Gegevens importeren",
        ["WfStep2Desc"] = "Importeer het JSON-bestand met de clubgegevens.",
        ["WfStep2Button"] = "Naar Gegevensbeheer",
        ["WfStep3Title"] = "Stap 3: Zitting instellen",
        ["WfStep3Desc"] = "Stel de aanwezigheid in, genereer de tafelindeling en sla de zitting op.",
        ["WfStep3Button"] = "Naar Zitting",
        ["WfStep4Title"] = "Stap 4: Gegevens exporteren",
        ["WfStep4Desc"] = "Exporteer de bijgewerkte gegevens naar een JSON-bestand.",
        ["WfStep4Button"] = "Naar Gegevensbeheer",
        ["WfStep5Title"] = "Stap 5: Clubgegevens terugzetten",
        ["WfStep5Desc"] = "Kopieer het geëxporteerde bestand uit de Downloads-map naar de map \"Competitiecommissie Tsumo 2025\".",
        ["ResetWorkflow"] = "Stappenplan resetten",
        ["ResetWorkflowHint"] = "Beschikbaar wanneer alle stappen zijn afgerond.",
        ["BackToWorkflow"] = "Terug naar stappenplan",
        ["BackToWorkflowHint"] = "Sla eerst de zitting op.",
        ["BackToWorkflowImportHint"] = "Importeer eerst een bestand.",
        ["BackToWorkflowExportHint"] = "Exporteer eerst de gegevens.",

        ["ExcludeFromOptimization"] = "Uitsluiten van indelingsoptimalisatie",
        ["ExcludedFromOptimization"] = "Uitgesloten",

        ["Monday"] = "Maandag",
        ["Tuesday"] = "Dinsdag",
        ["Wednesday"] = "Woensdag",
        ["Thursday"] = "Donderdag",
        ["Friday"] = "Vrijdag",
        ["Saturday"] = "Zaterdag",
        ["Sunday"] = "Zondag",

        ["Month1"] = "januari",
        ["Month2"] = "februari",
        ["Month3"] = "maart",
        ["Month4"] = "april",
        ["Month5"] = "mei",
        ["Month6"] = "juni",
        ["Month7"] = "juli",
        ["Month8"] = "augustus",
        ["Month9"] = "september",
        ["Month10"] = "oktober",
        ["Month11"] = "november",
        ["Month12"] = "december",
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
        ["WorkflowHomeDesc"] = "Follow the step-by-step guide to prepare and finalize a session.",
        ["StartWorkflow"] = "Start workflow",

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
        ["ExtraThreePlayerTables"] = "Extra 3-player tables",
        ["ExtraThreePlayerTablesDesc"] = "Number of 3-player tables not covered in session history.",

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

        // Settings page
        ["Settings"] = "Settings",
        ["PlayingDays"] = "Playing Days",
        ["PlayingDaysDesc"] = "Select the days the club plays and set the starting times.",
        ["AddTime"] = "Add time",
        ["SettingsSaved"] = "Settings saved!",

        // Workflow page
        ["Workflow"] = "Workflow",
        ["WorkflowDesc"] = "Follow the steps below to prepare and finalize a session.",
        ["WfStep1Title"] = "Step 1: Copy club data",
        ["WfStep1Desc"] = "Copy the club data from the \"Competitiecommissie Tsumo 2025\" folder to a local folder.",
        ["WfStep2Title"] = "Step 2: Import data",
        ["WfStep2Desc"] = "Import the JSON file with the club data.",
        ["WfStep2Button"] = "Go to Data Management",
        ["WfStep3Title"] = "Step 3: Set up the session",
        ["WfStep3Desc"] = "Administer attendance, generate table assignments, and save the session.",
        ["WfStep3Button"] = "Go to Session",
        ["WfStep4Title"] = "Step 4: Export data",
        ["WfStep4Desc"] = "Export the updated data to a JSON file.",
        ["WfStep4Button"] = "Go to Data Management",
        ["WfStep5Title"] = "Step 5: Copy club data back",
        ["WfStep5Desc"] = "Copy the exported file from the Downloads folder to the \"Competitiecommissie Tsumo 2025\" folder.",
        ["ResetWorkflow"] = "Reset workflow",
        ["ResetWorkflowHint"] = "Available when all steps are completed.",
        ["BackToWorkflow"] = "Back to workflow",
        ["BackToWorkflowHint"] = "Save the session first.",
        ["BackToWorkflowImportHint"] = "Import a file first.",
        ["BackToWorkflowExportHint"] = "Export the data first.",

        ["ExcludeFromOptimization"] = "Exclude from meeting optimization",
        ["ExcludedFromOptimization"] = "Excluded",

        ["Monday"] = "Monday",
        ["Tuesday"] = "Tuesday",
        ["Wednesday"] = "Wednesday",
        ["Thursday"] = "Thursday",
        ["Friday"] = "Friday",
        ["Saturday"] = "Saturday",
        ["Sunday"] = "Sunday",

        ["Month1"] = "January",
        ["Month2"] = "February",
        ["Month3"] = "March",
        ["Month4"] = "April",
        ["Month5"] = "May",
        ["Month6"] = "June",
        ["Month7"] = "July",
        ["Month8"] = "August",
        ["Month9"] = "September",
        ["Month10"] = "October",
        ["Month11"] = "November",
        ["Month12"] = "December",
    };
}
