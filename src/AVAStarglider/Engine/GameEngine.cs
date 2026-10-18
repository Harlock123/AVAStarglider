namespace AVAStarglider.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The wave named; a moment before it starts.</summary>
    Start,
    Play,
    /// <summary>The AGAV lost: a moment, then a new one, or the end.</summary>
    Dying,
    /// <summary>The Starglider down: the energy left counted in, then the next wave.</summary>
    Cleared,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Laser, Hit, Boom, Deflect, Launch, StargliderHit, StargliderDown, EgronFire, Hurt, Explode, Bump, Dock, Charge, LowEnergy,
    SayStarglider,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 70;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.4, DyingSeconds = 2.6, ClearedSeconds = 5;
    public const int Lives0 = 3, EnergyPoints = 20, ExtraEvery = 20000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _lowWarned, _charging;
    private int _nextExtra;
    private int _seed;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    /// <summary>AGAVs left, the one flying among them.</summary>
    public int Lives { get; private set; }
    public int Score => World.Score;
    public int Level { get; private set; }
    /// <summary>Starglider waves cleared this game (each one's Starglider brought down).</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    private void Enter(int level)
    {
        Level = level;
        int score = World.Score, missiles = World.Agav.Missiles;
        Sim.Start(World, NoveniaBook.Build(level), _seed * 31 + level);
        World.Score = score;
        if (level > 0) World.Agav.Missiles = Math.Max(missiles, 2);
        _lowWarned = false;
        SetPhase(Phase.Start);
        Say($"WAVE {level + 1}", StartSeconds);
        Pilot.Reset();
    }


    private void Say(string text, double seconds)
    {
        Message = text;
        MessageTime = seconds;
    }

    public void StartGame()
    {
        State = GameState.Playing;
        NewGame();
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        _seed++;
        NewGame();
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); }
                break;
            case Phase.Play:
            {
                Sim.Step(w, c);
                ExtraLives();
                var ag = w.Agav;
                if (ag.Energy < 25 && !_lowWarned && !ag.Dead) { _lowWarned = true; Play(Sfx.LowEnergy); Say("ENERGY LOW", 2); }
                if (ag.Energy > 50) _lowWarned = false;
                if (w.Charging != _charging) { _charging = w.Charging; if (_charging) Play(Sfx.Charge); }
                if (ag.Dead) { SetPhase(Phase.Dying); Music(false); Say(w.Cause, DyingSeconds); }
                else if (w.StargliderDown)
                {
                    SetPhase(Phase.Cleared);
                    Music(false);
                    int bonus = (int)ag.Energy * EnergyPoints + (Level + 1) * 1000;
                    w.Score += bonus;
                    Cleared++;
                    Play(Sfx.Victory);
                    Say($"THE STARGLIDER IS DOWN!  BONUS {bonus}", ClearedSeconds);
                }
                break;
            }
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { End(); return; }
                    Sim.Respawn(w);
                    Pilot.Reset();
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Cleared:
                if (PhaseTime > ClearedSeconds) Enter(Level + 1);
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
        }
    }

    private void End()
    {
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        Play(Sfx.GameOver);
        State = GameState.GameOver;
        StateTime = 0;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a wave, past its name.</summary>
    public void Go(int level = 0)
    {
        State = GameState.Playing;
        NewGame();
        if (level != 0) Enter(level);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
