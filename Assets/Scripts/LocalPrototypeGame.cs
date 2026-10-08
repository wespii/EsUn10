using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class LocalPrototypeGame : MonoBehaviour
{
    private const int MinimumPlayers = 2;
    private const int MaximumPlayers = 4;
    private const int MinimumNumber = 1;
    private const int MaximumNumber = 10;
    private const int MaximumClueLength = 120;
    private const float BotTurnDelay = 1.5f;
    private const float TurnDuration = 90f;
    private const int MaximumGuesses = 3;

    private enum GameScreen
    {
        MainMenu,
        Lobby,
        Game
    }

    [Serializable]
    private sealed class Player
    {
        public string Name;
        public int Score;
        public int SecretNumber;
        public bool IsLocal;

        public Player(string name, bool isLocal)
        {
            Name = name;
            IsLocal = isLocal;
        }
    }

    private static readonly string[] PlayerNames = { "Tú", "Lola", "Max", "Nico" };
    private static readonly string[] ClueEndings =
    {
        "siempre llega tarde.",
        "vive bastante lejos.",
        "nunca responde los mensajes.",
        "se come las patatas de los demás."
    };
    private static readonly string[] ClueCategories = { "HOT", "PAREJA", "AMIGOS", "BROMA" };
    private static readonly string[][][] ClueSuggestions =
    {
        new[]
        {
            new[] { "su foto de perfil parece tomada con una papa.", "coquetea mirando al piso.", "su mejor ángulo todavía está cargando." },
            new[] { "su sonrisa debería venir con advertencia.", "se arregla tanto que llega tarde a su propia cita.", "tiene más encanto que señal de Wi-Fi." },
            new[] { "entra a un lugar y hasta la playlist se pone romántica.", "su mirada dura más que una canción de amor.", "podría conseguir una cita con solo decir hola." },
            new[] { "parece protagonista de una película romántica y lo sabe.", "su voz debería tener su propio club de fans.", "hasta el espejo le pide una cita." }
        },
        new[]
        {
            new[] { "responde 'ok' y desaparece tres días.", "olvida la fecha de aniversario aunque se la recuerdes ese mismo día.", "dice 'ya voy' cuando todavía no se ha cambiado." },
            new[] { "se roba tus papas y luego ofrece compartir las suyas.", "elige la película y se duerme a los diez minutos.", "pregunta qué quieres comer y rechaza todas las opciones." },
            new[] { "recuerda cómo pides el café, pero no dónde dejó las llaves.", "te guarda el último pedazo de postre (a veces).", "manda memes justo cuando necesitas reírte." },
            new[] { "te conoce tanto que ya sabe qué vas a pedir antes que tú.", "convierte un mandado aburrido en una cita improvisada.", "te hace reír incluso cuando tiene razón en una discusión." }
        },
        new[]
        {
            new[] { "dice 'cinco minutos' y aparece al día siguiente.", "nunca devuelve el recipiente, pero sí lo publica en historias.", "te deja en visto y luego pregunta por qué no respondiste." },
            new[] { "siempre tiene un plan, aunque nadie sepa cuál es.", "te presta algo y te lo recuerda en cada reunión.", "manda audios de tres minutos para decir 'sí'." },
            new[] { "comparte la comida sin preguntar cuánto te serviste.", "sabe cuándo necesitas compañía y cuándo necesitas pizza.", "se acuerda de tus historias aunque las hayas contado cinco veces." },
            new[] { "te cubriría una coartada, pero se reiría en medio de ella.", "es la razón por la que el grupo tiene un chat aparte.", "llega con snacks y se va con tu cargador." }
        },
        new[]
        {
            new[] { "se ríe de sus propios chistes antes de contarlos.", "perdería una carrera contra una fila de banco.", "su superpoder es preguntar '¿qué?' y entender todo." },
            new[] { "tiene una historia para todo y ninguna termina donde empezó.", "usa la calculadora para dividir una cuenta entre dos.", "aplaude cuando aterriza el avión." },
            new[] { "hace chistes malos con tanta confianza que casi funcionan.", "podría perderse usando el GPS en línea recta.", "le habla a las plantas y espera que le contesten." },
            new[] { "convierte cualquier silencio incómodo en un show de comedia.", "podría hacer reír hasta al tutorial de términos y condiciones.", "tiene energía de protagonista incluso haciendo fila." }
        }
    };

    private readonly List<Player> players = new List<Player>();
    private readonly List<string> clues = new List<string>();
    private readonly System.Random random = new System.Random();

    private GameScreen currentScreen = GameScreen.MainMenu;
    private int playerCount = 3;
    private int activePlayerIndex;
    private int selectedGuess;
    private int guessesRemaining = MaximumGuesses;
    private int selectedClueCategory;
    private string clueSuggestion = string.Empty;
    private string clueText = string.Empty;
    private string onlineServerUrl = "http://localhost:5080";
    private string resultMessage = string.Empty;
    private float resultUntil;
    private float botTurnAt;
    private float turnStartedAt;
    private int roundNumber;
    private bool showHelp;
    private Vector2 clueScroll;
    private GUIStyle titleStyle;
    private GUIStyle panelStyle;
    private GUIStyle cardStyle;
    private GUIStyle activeCardStyle;
    private GUIStyle buttonStyle;
    private GUIStyle smallLabelStyle;
    private GUIStyle headerStyle;
    private GUIStyle bodyStyle;
    private GUIStyle accentStyle;
    private GUIStyle mutedStyle;
    private GUIStyle numberStyle;
    private GUIStyle selectedNumberStyle;
    private GUIStyle dangerButtonStyle;
    private GUIStyle menuButtonStyle;
    private GUIStyle tableStyle;
    private GUIStyle seatStyle;
    private GUIStyle activeSeatStyle;
    private GUIStyle avatarStyle;
    private GUIStyle activeAvatarStyle;
    private GUIStyle secretCardStyle;
    private GUIStyle visibleCardStyle;
    private GUIStyle focusStyle;
    private GUIStyle timerStyle;
    private GUIStyle inputStyle;
    private GUIStyle bubbleStyle;
    private GUIStyle playerNameStyle;
    private GUIStyle scoreStyle;
    private GUIStyle connectionStyle;
    private GUIStyle eyebrowStyle;
    private GUIStyle largeNumberStyle;
    private GUIStyle secretBigNumberStyle;
    private GUIStyle brandStyle;
    private GUIStyle heroStyle;
    private Texture2D tableTexture;
    private Texture2D tableShadowTexture;
    private Texture2D blobTexture;
    private Texture2D cardBackTexture;
    private Texture2D cardFrontTexture;
    private int styledWidth;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindObjectOfType<LocalPrototypeGame>() == null)
        {
            new GameObject("Local Prototype Game").AddComponent<LocalPrototypeGame>();
        }
    }

    private void Awake()
    {
        onlineServerUrl = PlayerPrefs.GetString("online_server_url", onlineServerUrl);
        // The prototype scene has no world objects, but Game view still needs a camera
        // to avoid Unity's "Display 1 - No cameras rendering" overlay over the IMGUI.
        if (FindObjectOfType<Camera>() == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            Camera gameCamera = cameraObject.AddComponent<Camera>();
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = new Color(247f / 255f, 225f / 255f, 230f / 255f);
            gameCamera.orthographic = true;
            gameCamera.depth = -1f;
        }
    }

    private void OnGUI()
    {
        EnsureStyles();
        DrawBackground();

        if (currentScreen == GameScreen.MainMenu)
        {
            DrawMainMenu();
        }
        else if (currentScreen == GameScreen.Lobby)
        {
            DrawLobby();
        }
        else
        {
            DrawGame();
        }
    }

    private void Update()
    {
        if (currentScreen == GameScreen.Game &&
            players.Count > 0 &&
            !players[activePlayerIndex].IsLocal &&
            Time.time >= botTurnAt)
        {
            ResolveGuess(random.Next(MinimumNumber, MaximumNumber + 1));
        }

        if (currentScreen == GameScreen.Game &&
            players.Count > 0 &&
            players[activePlayerIndex].IsLocal &&
            Time.time - turnStartedAt >= TurnDuration)
        {
            SkipTurn();
        }
    }

    private void EnsureStyles()
    {
        if (styledWidth == Screen.width && titleStyle != null)
        {
            return;
        }

        float scale = Mathf.Clamp(Screen.width / 1280f, 0.72f, 1.15f);
        float readableScale = Mathf.Max(scale, 0.84f);

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(48f * scale),
            fontStyle = FontStyle.Bold
        };
        titleStyle.normal.textColor = Hex("#F8F1E4");

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8F1E4"));
        panelStyle.border = new RectOffset(14, 14, 14, 14);
        panelStyle.margin = new RectOffset(0, 0, 0, 0);
        panelStyle.padding = new RectOffset(0, 0, 0, 0);

        cardStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(17f * scale),
            fontStyle = FontStyle.Bold
        };
        cardStyle.normal.textColor = Hex("#183F38");
        cardStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8F1E4"));
        cardStyle.border = new RectOffset(14, 14, 14, 14);

        activeCardStyle = new GUIStyle(cardStyle);
        activeCardStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F26852"));

        buttonStyle = FlatButton(Hex("#F26852"), Hex("#FF8068"), Hex("#D94F3D"));
        buttonStyle.fontSize = Mathf.RoundToInt(16f * readableScale);
        buttonStyle.normal.textColor = Hex("#FFF8EC");
        buttonStyle.hover.textColor = Hex("#FFF8EC");
        buttonStyle.active.textColor = Hex("#FFF8EC");

        smallLabelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(14f * readableScale)
        };
        smallLabelStyle.normal.textColor = Hex("#52736A");

        headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(20f * readableScale),
            fontStyle = FontStyle.Bold
        };
        headerStyle.normal.textColor = Hex("#183F38");

        bodyStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(16f * readableScale),
            wordWrap = true
        };
        bodyStyle.normal.textColor = Hex("#294D45");

        accentStyle = new GUIStyle(headerStyle);
        accentStyle.normal.textColor = Hex("#C17A1A");

        mutedStyle = new GUIStyle(smallLabelStyle);
        mutedStyle.normal.textColor = Hex("#668078");

        numberStyle = FlatButton(Hex("#FFF8EC"), Hex("#F8D98A"), Hex("#EEC565"));
        numberStyle.fontSize = Mathf.RoundToInt(18f * readableScale);
        numberStyle.normal.textColor = Hex("#183F38");

        selectedNumberStyle = new GUIStyle(numberStyle);
        selectedNumberStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8D98A"));
        selectedNumberStyle.normal.textColor = Hex("#183F38");
        selectedNumberStyle.border = new RectOffset(14, 14, 14, 14);

        dangerButtonStyle = FlatButton(Hex("#E9E1D2"), Hex("#F2EBDD"), Hex("#D8CDBB"));
        dangerButtonStyle.normal.textColor = Hex("#52736A");

        menuButtonStyle = FlatButton(Hex("#DCE9DD"), Hex("#D0E2D2"), Hex("#BED5C1"));
        menuButtonStyle.normal.textColor = Hex("#183F38");

        inputStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = Mathf.RoundToInt(15f * readableScale),
            alignment = TextAnchor.MiddleLeft,
            border = new RectOffset(0, 0, 0, 0),
            padding = new RectOffset(12, 12, 6, 6)
        };
        inputStyle.normal.background = MakeTexture(Hex("#FFF8EC"));
        inputStyle.focused.background = MakeTexture(Hex("#F8D98A"));
        inputStyle.normal.textColor = Hex("#183F38");
        inputStyle.focused.textColor = Hex("#183F38");

        tableStyle = new GUIStyle(GUI.skin.box);
        tableStyle.normal.background = MakeTexture(Hex("#244E45"));
        tableStyle.border = new RectOffset(0, 0, 0, 0);

        seatStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        seatStyle.normal.textColor = Hex("#183F38");
        seatStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8F1E4"));
        seatStyle.border = new RectOffset(14, 14, 14, 14);

        activeSeatStyle = new GUIStyle(seatStyle);
        activeSeatStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8D98A"));

        avatarStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 19,
            fontStyle = FontStyle.Bold
        };
        avatarStyle.normal.textColor = Hex("#183F38");
        avatarStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#DCE9DD"));
        avatarStyle.border = new RectOffset(14, 14, 14, 14);

        activeAvatarStyle = new GUIStyle(avatarStyle);
        activeAvatarStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F26852"));

        secretCardStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 22,
            fontStyle = FontStyle.Bold
        };
        secretCardStyle.normal.textColor = Hex("#F26852");
        secretCardStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#FFF8EC"));
        secretCardStyle.border = new RectOffset(14, 14, 14, 14);

        visibleCardStyle = new GUIStyle(secretCardStyle);
        visibleCardStyle.normal.textColor = Hex("#183F38");
        visibleCardStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#F8D98A"));
        visibleCardStyle.border = new RectOffset(14, 14, 14, 14);

        focusStyle = new GUIStyle(headerStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(22f * readableScale)
        };
        focusStyle.normal.textColor = Hex("#183F38");

        timerStyle = new GUIStyle(accentStyle)
        {
            alignment = TextAnchor.MiddleRight,
            fontSize = Mathf.RoundToInt(18f * readableScale)
        };
        bubbleStyle = new GUIStyle(bodyStyle)
        {
            padding = new RectOffset(14, 14, 8, 8)
        };
        bubbleStyle.normal.background = MakeRoundedTexture(64, 64, Hex("#FFF8EC"));
        bubbleStyle.border = new RectOffset(14, 14, 14, 14);
        playerNameStyle = new GUIStyle(headerStyle)
        {
            fontSize = Mathf.RoundToInt(15f * readableScale),
            alignment = TextAnchor.MiddleCenter
        };
        scoreStyle = new GUIStyle(smallLabelStyle)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold
        };
        connectionStyle = new GUIStyle(smallLabelStyle)
        {
            fontSize = Mathf.RoundToInt(10f * readableScale),
            alignment = TextAnchor.MiddleCenter
        };
        eyebrowStyle = new GUIStyle(smallLabelStyle)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            fontSize = Mathf.RoundToInt(12f * readableScale)
        };
        eyebrowStyle.normal.textColor = Hex("#F26852");
        largeNumberStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.RoundToInt(112f * readableScale),
            fontStyle = FontStyle.Bold
        };
        largeNumberStyle.normal.textColor = Hex("#183F38");
        secretBigNumberStyle = new GUIStyle(largeNumberStyle);
        secretBigNumberStyle.normal.textColor = Hex("#FFF8EC");
        brandStyle = new GUIStyle(titleStyle)
        {
            fontSize = Mathf.RoundToInt(27f * readableScale),
            alignment = TextAnchor.MiddleLeft
        };
        brandStyle.normal.textColor = Hex("#F8F1E4");
        heroStyle = new GUIStyle(titleStyle);
        heroStyle.normal.textColor = Hex("#183F38");
        tableTexture = MakeEllipseTexture(256, 128, Hex("#244E45"));
        tableShadowTexture = MakeEllipseTexture(256, 128, new Color(0.2f, 0.1f, 0.2f, 0.16f));
        blobTexture = MakeEllipseTexture(256, 256, new Color(0.8f, 0.9f, 0.7f, 0.08f));
        cardBackTexture = MakeRoundedTexture(128, 160, Hex("#F26852"));
        cardFrontTexture = MakeRoundedTexture(128, 160, Hex("#FFF8EC"));
        styledWidth = Screen.width;
    }

    private GUIStyle FlatButton(Color normal, Color hover, Color active)
    {
        GUIStyle style = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            border = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(3, 3, 3, 3),
            padding = new RectOffset(8, 8, 6, 6)
        };
        style.normal.background = MakeTexture(normal);
        style.hover.background = MakeTexture(hover);
        style.active.background = MakeTexture(active);
        style.focused.background = style.hover.background;
        style.normal.textColor = Hex("#624B72");
        style.hover.textColor = Hex("#624B72");
        style.active.textColor = Hex("#624B72");
        style.focused.textColor = Hex("#624B72");
        return style;
    }

    private void DrawBackground()
    {
        GUI.color = Hex("#183F38");
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.16f);
        GUI.DrawTexture(new Rect(-120, -120, 360, 360), blobTexture);
        GUI.DrawTexture(new Rect(Screen.width - 220, Screen.height - 180, 320, 320), blobTexture);
        GUI.color = Color.white;
    }

    private void DrawMainMenu()
    {
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1000f, Screen.height / 650f), 0.58f, 1f);
        float width = Mathf.Min(1120f * scale, Screen.width - 28f);
        float height = Mathf.Min(690f * scale, Screen.height - 28f);
        Rect panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panel.x + 32f * scale, panel.y + 20f * scale, panel.width * 0.65f, 24f * scale), "✦  EL JUEGO DE CARTAS PARA TU GRUPO  ✦", eyebrowStyle);
        if (showHelp)
        {
            GUI.Label(new Rect(panel.x + 32f * scale, panel.y + 66f * scale, panel.width - 64f * scale, 42f * scale), "ASÍ SE JUEGA", headerStyle);
            GUI.Label(new Rect(panel.x + 32f * scale, panel.y + 112f * scale, panel.width - 64f * scale, 170f * scale),
                "1. A una persona le toca una carta secreta del 1 al 10.\n\n2. El resto da pistas en voz alta: “Es un 10, pero…”.\n\n3. La persona tiene 90 segundos y hasta 3 intentos para descubrir su número.", bodyStyle);
            if (GUI.Button(new Rect(panel.x + 32f * scale, panel.y + height - 74f * scale, 190f * scale, 44f * scale), "VOLVER A LA MESA", menuButtonStyle)) showHelp = false;
            DrawMenuCard(new Rect(panel.x + panel.width * 0.67f, panel.y + 58f * scale, panel.width * 0.25f, panel.height * 0.70f), scale);
        }
        else
        {
            float leftX = panel.x + 36f * scale;
            float leftWidth = panel.width * 0.56f;
            GUI.Label(new Rect(leftX, panel.y + 70f * scale, leftWidth, 46f * scale), "ES UN 10", heroStyle);
            GUI.Label(new Rect(leftX, panel.y + 116f * scale, leftWidth, 58f * scale), "PERO...", heroStyle);
            GUI.Label(new Rect(leftX, panel.y + 182f * scale, leftWidth - 12f, 56f * scale), "Una carta secreta.\nUn grupo lleno de teorías.", bodyStyle);
            GUI.Label(new Rect(leftX, panel.y + 252f * scale, leftWidth, 28f * scale), "¿CUÁNTOS SE SIENTAN?", eyebrowStyle);
            for (int i = 2; i <= 4; i++)
            {
                float w = 68f * scale;
                Rect choice = new Rect(leftX + (i - 2) * (w + 9f * scale), panel.y + 286f * scale, w, 46f * scale);
                if (GUI.Button(choice, i + "", i == playerCount ? selectedNumberStyle : menuButtonStyle)) playerCount = i;
            }
            if (GUI.Button(new Rect(leftX, panel.y + height - 116f * scale, leftWidth - 24f * scale, 58f * scale), "REUNIR AL GRUPO   →", buttonStyle)) CreateLocalLobby();
            GUI.Label(new Rect(leftX, panel.y + height - 226f * scale, leftWidth, 20f * scale), "JUGAR ONLINE", eyebrowStyle);
            GUI.SetNextControlName("OnlineServerUrl");
            onlineServerUrl = GUI.TextField(new Rect(leftX, panel.y + height - 202f * scale, leftWidth - 24f * scale, 30f * scale), onlineServerUrl, inputStyle);
            if (GUI.Button(new Rect(leftX, panel.y + height - 164f * scale, leftWidth - 24f * scale, 42f * scale), "ABRIR MESA ONLINE   ↗", menuButtonStyle)) OpenOnlineGame();
            if (GUI.Button(new Rect(leftX, panel.y + height - 54f * scale, leftWidth - 24f * scale, 32f * scale), "CÓMO SE JUEGA", menuButtonStyle)) showHelp = true;
            DrawMenuCard(new Rect(panel.x + panel.width * 0.67f, panel.y + 56f * scale, panel.width * 0.25f, panel.height * 0.72f), scale);
        }
        GUI.Label(new Rect(panel.x + 32f * scale, panel.y + height - 23f * scale, panel.width - 64f * scale, 18f * scale), "PARTIDA LOCAL  •  2–4 PERSONAS  •  90 SEGUNDOS POR TURNO", mutedStyle);
    }

    private void OpenOnlineGame()
    {
        string candidate = onlineServerUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Debug.LogWarning("Introduce la URL https del servidor online.");
            return;
        }

        onlineServerUrl = candidate;
        PlayerPrefs.SetString("online_server_url", onlineServerUrl);
        PlayerPrefs.Save();
        Application.OpenURL(onlineServerUrl);
    }

    private void DrawMenuCard(Rect rect, float scale)
    {
        GUI.DrawTexture(new Rect(rect.x + 10f * scale, rect.y + 12f * scale, rect.width, rect.height), cardBackTexture);
        GUI.DrawTexture(rect, cardFrontTexture);
        GUI.Label(new Rect(rect.x + 16f * scale, rect.y + 18f * scale, rect.width - 32f * scale, 24f * scale), "CARTA SECRETA", eyebrowStyle);
        GUI.Label(new Rect(rect.x + 10f * scale, rect.y + rect.height * 0.23f, rect.width - 20f * scale, rect.height * 0.43f), "10", largeNumberStyle);
        GUI.Label(new Rect(rect.x + 18f * scale, rect.y + rect.height - 84f * scale, rect.width - 36f * scale, 44f * scale), "¿Un diez perfecto?\nSiempre hay un pero.", bodyStyle);
        GUI.Label(new Rect(rect.x + rect.width - 40f * scale, rect.y + rect.height - 40f * scale, 24f * scale, 24f * scale), "✦", accentStyle);
    }

    private void DrawGame()
    {
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1000f, Screen.height / 650f), 0.58f, 1f);
        GUI.Label(new Rect(22f * scale, 9f * scale, Screen.width * 0.54f, 38f * scale), "ES UN 10 PERO...", brandStyle);
        GUI.Label(new Rect(22f * scale, 46f * scale, Screen.width * 0.54f, 22f * scale), "RONDA " + roundNumber.ToString("00") + "   /   " + players.Count + " EN LA MESA", eyebrowStyle);
        Player active = players[activePlayerIndex];
        float remaining = Mathf.Max(0f, TurnDuration - (Time.time - turnStartedAt));
        float timerWidth = 116f * scale;
        Rect timer = new Rect(Screen.width - timerWidth - 20f * scale, 15f * scale, timerWidth, 43f * scale);
        GUI.Box(timer, GUIContent.none, active.IsLocal ? selectedNumberStyle : menuButtonStyle);
        GUI.Label(timer, "◷  " + Mathf.FloorToInt(remaining / 60f) + ":" + Mathf.CeilToInt(remaining % 60f).ToString("00"), timerStyle);

        DrawPlayerRibbon(scale);
        float cardWidth = Mathf.Min(270f * scale, Screen.width * 0.36f);
        float cardHeight = Mathf.Min(310f * scale, Screen.height * 0.49f);
        Rect card = new Rect((Screen.width - cardWidth) * 0.5f, Screen.height * 0.17f, cardWidth, cardHeight);
        DrawTurnCard(active, card, scale);
        if (active.IsLocal)
        {
            DrawGuessPanel(new Rect(Screen.width * 0.05f, Screen.height - 146f * scale, Screen.width * 0.90f, 132f * scale), scale);
        }
        else
        {
            DrawClueHelper(new Rect(Screen.width * 0.05f, Screen.height - 156f * scale, Screen.width * 0.90f, 142f * scale), active, scale);
        }

        if (Time.time < resultUntil)
        {
            float resultWidth = Mathf.Min(420f * scale, Screen.width - 40f);
            Rect result = new Rect((Screen.width - resultWidth) * 0.5f, Screen.height * 0.5f - 68f * scale, resultWidth, 136f * scale);
            GUI.Box(result, GUIContent.none, panelStyle);
            GUI.Label(result, resultMessage, focusStyle);
        }
    }

    private void DrawPlayerRibbon(float scale)
    {
        float width = Mathf.Min(470f * scale, Screen.width * 0.70f);
        float chipWidth = width / players.Count;
        float x = (Screen.width - width) * 0.5f;
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            Rect chip = new Rect(x + i * chipWidth + 3f * scale, 72f * scale, chipWidth - 6f * scale, 31f * scale);
            GUI.Box(chip, GUIContent.none, i == activePlayerIndex ? selectedNumberStyle : menuButtonStyle);
            GUI.Label(chip, GetInitials(player.Name) + "   " + player.Name + (i == activePlayerIndex ? "  •  TURNO" : ""), i == activePlayerIndex ? headerStyle : smallLabelStyle);
        }
    }

    private void DrawClueHelper(Rect area, Player active, float scale)
    {
        GUI.Box(area, GUIContent.none, panelStyle);
        GUI.Label(new Rect(area.x + 14f * scale, area.y + 6f * scale, area.width * 0.34f, 24f * scale), "DA UNA PISTA", eyebrowStyle);
        float gap = 4f * scale;
        float categoryWidth = Mathf.Min(86f * scale, (area.width * 0.48f - gap * 3f) / 4f);
        for (int i = 0; i < ClueCategories.Length; i++)
        {
            Rect categoryRect = new Rect(area.x + 12f * scale + i * (categoryWidth + gap), area.y + 35f * scale, categoryWidth, 31f * scale);
            if (GUI.Button(categoryRect, ClueCategories[i], i == selectedClueCategory ? selectedNumberStyle : menuButtonStyle))
            {
                selectedClueCategory = i;
                clueSuggestion = string.Empty;
            }
        }

        float buttonX = area.x + area.width * 0.54f;
        Rect button = new Rect(buttonX, area.y + 29f * scale, area.width * 0.43f - 12f * scale, 42f * scale);
        GUI.enabled = players.Count > 0 && !active.IsLocal;
        if (GUI.Button(button, "✦  SUGIÉREME UNA PISTA", buttonStyle)) GenerateClueSuggestion(active);
        GUI.enabled = true;

        Rect suggestionBox = new Rect(area.x + 12f * scale, area.y + 76f * scale, area.width - 24f * scale, area.height - 84f * scale);
        GUI.Box(suggestionBox, GUIContent.none, bubbleStyle);
        string text = string.IsNullOrEmpty(clueSuggestion)
            ? "Elige un estilo y pide una idea para decirla por voz."
            : "Es un 10, pero " + clueSuggestion;
        GUI.Label(new Rect(suggestionBox.x + 10f * scale, suggestionBox.y + 3f * scale, suggestionBox.width - 20f * scale, suggestionBox.height - 6f * scale), text, bodyStyle);
    }

    private void GenerateClueSuggestion(Player active)
    {
        int band = active.SecretNumber <= 3 ? 0 : active.SecretNumber <= 6 ? 1 : active.SecretNumber <= 8 ? 2 : 3;
        string[] options = ClueSuggestions[selectedClueCategory][band];
        clueSuggestion = options[random.Next(options.Length)];
    }

    private void DrawLobby()
    {
        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1000f, Screen.height / 650f), 0.58f, 1f);
        float width = Mathf.Min(880f * scale, Screen.width - 28f);
        float height = Mathf.Min(640f * scale, Screen.height - 28f);
        Rect panel = new Rect((Screen.width - width) / 2, (Screen.height - height) / 2, width, height);
        GUI.Box(panel, GUIContent.none, panelStyle);
        GUI.Label(new Rect(panel.x + 28f * scale, panel.y + 20f * scale, panel.width - 56f * scale, 24f * scale), "✦  LA MESA ESTÁ CASI LISTA", eyebrowStyle);
        GUI.Label(new Rect(panel.x + 28f * scale, panel.y + 47f * scale, panel.width - 56f * scale, 40f * scale), "REÚNE A TU GRUPO", headerStyle);
        GUI.Label(new Rect(panel.x + 28f * scale, panel.y + 88f * scale, panel.width - 56f * scale, 25f * scale), "Una carta para adivinar. Muchas versiones de la historia.", mutedStyle);
        float gap = 10f * scale;
        float slotWidth = (panel.width - 56f * scale - gap) * 0.5f;
        float slotHeight = Mathf.Min(64f * scale, (height - 220f * scale) * 0.5f);
        for (int i = 0; i < playerCount; i++)
        {
            string name = i < players.Count ? players[i].Name : "Esperando jugador...";
            int column = i % 2;
            int row = i / 2;
            Rect slot = new Rect(panel.x + 28f * scale + column * (slotWidth + gap), panel.y + 132f * scale + row * (slotHeight + gap), slotWidth, slotHeight);
            GUI.Box(slot, GUIContent.none, i == 0 ? selectedNumberStyle : menuButtonStyle);
            GUI.Box(new Rect(slot.x + 10f * scale, slot.y + 10f * scale, 42f * scale, 42f * scale), GetInitials(name), i == 0 ? activeAvatarStyle : avatarStyle);
            GUI.Label(new Rect(slot.x + 62f * scale, slot.y + 7f * scale, slot.width - 76f * scale, 25f * scale), name, headerStyle);
            GUI.Label(new Rect(slot.x + 62f * scale, slot.y + 32f * scale, slot.width - 76f * scale, 20f * scale), i == 0 ? "ANFITRIÓN  •  TÚ" : "BOT DE PRUEBA", mutedStyle);
        }
        Rect howTo = new Rect(panel.x + 28f * scale, panel.y + height * 0.59f, panel.width - 56f * scale, 74f * scale);
        GUI.Box(howTo, GUIContent.none, menuButtonStyle);
        string[] steps = { "01  ESCUCHA", "02  DA PISTAS", "03  ADIVINA" };
        string[] stepCopy = { "La carta es secreta", "Hazlo por voz", "Tienes 3 intentos" };
        float stepWidth = howTo.width / 3f;
        for (int i = 0; i < steps.Length; i++)
        {
            float x = howTo.x + i * stepWidth + 12f * scale;
            GUI.Label(new Rect(x, howTo.y + 9f * scale, stepWidth - 24f * scale, 22f * scale), steps[i], headerStyle);
            GUI.Label(new Rect(x, howTo.y + 35f * scale, stepWidth - 24f * scale, 22f * scale), stepCopy[i], mutedStyle);
        }
        Rect start = new Rect(panel.x + 28f * scale, panel.y + height - 94f * scale, panel.width - 56f * scale, 52f * scale);
        if (GUI.Button(start, "REPARTIR LAS CARTAS   →", buttonStyle)) StartLocalGame();
        if (GUI.Button(new Rect(panel.x + 28f * scale, panel.y + height - 36f * scale, 130f * scale, 26f * scale), "← VOLVER", dangerButtonStyle)) currentScreen = GameScreen.MainMenu;
        GUI.Label(new Rect(panel.x + panel.width - 250f * scale, panel.y + height - 35f * scale, 220f * scale, 24f * scale), "MODO LOCAL  •  " + playerCount + " JUGADORES", mutedStyle);
    }

    private void DrawTurnCard(Player active, Rect card, float scale)
    {
        bool guessing = active.IsLocal;
        GUI.DrawTexture(new Rect(card.x + 7f * scale, card.y + 9f * scale, card.width, card.height), tableShadowTexture);
        GUI.DrawTexture(card, guessing ? cardBackTexture : cardFrontTexture);
        GUI.Label(new Rect(card.x + 15f * scale, card.y + 14f * scale, card.width - 30f * scale, 24f * scale), guessing ? "CARTA PARA TUS OJOS" : "LA CARTA DE " + active.Name.ToUpperInvariant(), eyebrowStyle);
        GUI.Label(new Rect(card.x + 10f * scale, card.y + card.height * 0.20f, card.width - 20f * scale, card.height * 0.48f), guessing ? "?" : active.SecretNumber.ToString(), guessing ? secretBigNumberStyle : largeNumberStyle);
        GUI.Label(new Rect(card.x + 18f * scale, card.y + card.height - 64f * scale, card.width - 36f * scale, 48f * scale), guessing ? "Escucha al grupo.\nTu número es secreto." : "¿Qué historia esconde\neste número?", bodyStyle);
        GUI.Label(new Rect(card.x + card.width - 38f * scale, card.y + card.height - 34f * scale, 24f * scale, 24f * scale), "✦", accentStyle);
    }

    private void CreateLocalLobby()
    {
        playerCount = Mathf.Clamp(playerCount, MinimumPlayers, MaximumPlayers);
        players.Clear();
        for (int i = 0; i < playerCount; i++) players.Add(new Player(PlayerNames[i], i == 0));
        currentScreen = GameScreen.Lobby;
    }

    private void DrawTable(Rect table)
    {
        GUI.Box(table, GUIContent.none, panelStyle);
        GUI.Label(new Rect(table.x + 15, table.y + 12, 300, 30), "MESA DE JUEGO", headerStyle);

        float centerX = table.x + table.width * 0.5f;
        float centerY = table.y + table.height * 0.56f;
        float radiusX = Mathf.Min(table.width * 0.30f, 300f);
        float radiusY = Mathf.Min(table.height * 0.31f, 82f);
        float tableSize = Mathf.Min(table.height - 45f, radiusY * 2f + 30f);
        Rect roundTable = new Rect(centerX - tableSize * 0.67f, centerY - tableSize * 0.5f, tableSize * 1.34f, tableSize);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(roundTable.x, roundTable.y + 9, roundTable.width, roundTable.height), tableShadowTexture);
        GUI.DrawTexture(roundTable, tableTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(centerX - 80, centerY - 15, 160, 30), "MESA", accentStyle);

        float seatWidth = Mathf.Clamp(table.width * 0.16f, 100f, 160f);
        float seatHeight = Mathf.Clamp(table.height * 0.2f, 46f, 58f);
        float cardWidth = Mathf.Clamp(table.width * 0.045f, 40f, 54f);
        float cardHeight = seatHeight;
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i];
            bool active = i == activePlayerIndex;
            string value = player.IsLocal ? "?" : player.SecretNumber.ToString();
            float angle = -Mathf.PI * 0.5f + (Mathf.PI * 2f * i / players.Count);
            float seatCenterX = centerX + Mathf.Cos(angle) * radiusX;
            float seatCenterY = centerY + Mathf.Sin(angle) * radiusY;
            float pulse = active ? 1f + Mathf.Sin(Time.time * 5f) * 0.035f : 1f;
            Rect seat = new Rect(seatCenterX - seatWidth * pulse * 0.5f, seatCenterY - seatHeight * pulse * 0.5f, seatWidth * pulse, seatHeight * pulse);
            Rect avatar = new Rect(seat.x - 22, seat.y + 1, 44, 44);
            Rect card = new Rect(seat.x + seat.width - cardWidth * 0.55f, seat.y - 14, cardWidth, cardHeight);

            GUI.Box(seat, GUIContent.none, active ? activeSeatStyle : seatStyle);
            GUI.Box(avatar, GetInitials(player.Name), active ? activeAvatarStyle : avatarStyle);
            GUI.Label(new Rect(seat.x + 18, seat.y + 3, seat.width - 22, 20), player.Name, playerNameStyle);
            GUI.Label(new Rect(seat.x + 18, seat.y + 22, seat.width - 22, 18), player.Score + " PTS", scoreStyle);
            GUI.Label(new Rect(seat.x + 18, seat.y + seat.height - 2, seat.width - 22, 17), "● CONECTADO", connectionStyle);
            GUI.DrawTexture(card, player.IsLocal ? cardBackTexture : cardFrontTexture);
            GUI.Label(new Rect(card.x, card.y + card.height * 0.31f, card.width, card.height * 0.38f), value, player.IsLocal ? secretCardStyle : visibleCardStyle);
            if (active)
            {
                GUI.Label(new Rect(seat.x - 38, seat.y - 32, seat.width + 76, 24), player.IsLocal ? "TU TURNO" : "PREGUNTAS", focusStyle);
            }
        }
        float remaining = Mathf.Max(0f, TurnDuration - (Time.time - turnStartedAt));
        GUI.Label(new Rect(table.x + table.width - 170, table.y + 15, 155, 28), "TIEMPO  " + Mathf.CeilToInt(remaining) + "s", timerStyle);
        Player activePlayer = players[activePlayerIndex];
        string prompt = activePlayer.IsLocal
            ? "ES TU TURNO: ESCUCHA LAS PISTAS Y ADIVINA"
            : "PREGUNTAS PARA " + activePlayer.Name.ToUpperInvariant();
        GUI.Box(new Rect(centerX - Mathf.Min(280f, table.width * 0.28f), table.y + table.height - 43f, Mathf.Min(560f, table.width * 0.56f), 32f), prompt, focusStyle);
    }

    private string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "?";
        }

        string[] parts = name.Trim().Split(' ');
        if (parts.Length == 1)
        {
            return parts[0].Substring(0, 1).ToUpperInvariant();
        }

        return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
    }

    private void DrawClues(Rect area)
    {
        GUI.Box(area, GUIContent.none, panelStyle);
        GUI.Label(new Rect(area.x + 15, area.y + 12, area.width - 30, 30), "PISTAS", headerStyle);
        GUI.Label(new Rect(area.x + 15, area.y + 37, area.width - 30, 24), "Es un 10, pero...", accentStyle);

        clueScroll = GUI.BeginScrollView(new Rect(area.x + 12, area.y + 66, area.width - 24, area.height - 130), clueScroll, new Rect(0, 0, area.width - 50, Mathf.Max(100, clues.Count * 60)));
        for (int i = 0; i < clues.Count; i++)
        {
            Rect bubble = new Rect(4, i * 56, area.width - 55, 48);
            GUI.Box(bubble, GUIContent.none, bubbleStyle);
            GUI.Label(new Rect(bubble.x + 10, bubble.y + 5, bubble.width - 20, 38), clues[i], bodyStyle);
        }
        GUI.EndScrollView();

        GUI.SetNextControlName("ClueInput");
        bool canClue = players.Count > 0 && players[activePlayerIndex].IsLocal;
        GUI.enabled = canClue;
        clueText = GUI.TextField(new Rect(area.x + 15, area.y + area.height - 52, area.width - 130, 34), clueText, inputStyle);
        if (GUI.Button(new Rect(area.x + area.width - 105, area.y + area.height - 52, 90, 34), "ENVIAR"))
        {
            SubmitClue();
        }
        GUI.enabled = true;
    }

    private void DrawGuessPanel(Rect area, float scale)
    {
        GUI.Box(area, GUIContent.none, panelStyle);
        Player active = players[activePlayerIndex];
        GUI.Label(new Rect(area.x + 12f * scale, area.y + 5f * scale, area.width * 0.26f, 25f * scale), "TU APUESTA", eyebrowStyle);
        GUI.Label(new Rect(area.x + area.width * 0.26f, area.y + 5f * scale, area.width * 0.28f, 25f * scale), "" + guessesRemaining + " INTENTOS", mutedStyle);

        int columns = 5;
        GUI.enabled = active.IsLocal;
        for (int i = 1; i <= 10; i++)
        {
            int column = (i - 1) % columns;
            int row = (i - 1) / columns;
            float numberWidth = area.width * 0.075f;
            float numberGap = 4f * scale;
            float startX = area.x + area.width * 0.025f + column * (numberWidth + numberGap);
            float y = area.y + 34f * scale + row * 34f * scale;
            Rect button = new Rect(startX, y, numberWidth, 29f * scale);
            if (GUI.Button(button, i.ToString(), i == selectedGuess ? selectedNumberStyle : numberStyle))
            {
                selectedGuess = i;
            }
        }

        float actionWidth = area.width * 0.34f;
        if (GUI.Button(new Rect(area.x + area.width - actionWidth - 12f * scale, area.y + 36f * scale, actionWidth, 64f * scale), "FIJAR MI NÚMERO\n→", buttonStyle))
        {
            MakeGuess();
        }
        GUI.enabled = true;
        if (GUI.Button(new Rect(area.x + area.width - 96f * scale, area.y + 5f * scale, 82f * scale, 24f * scale), "SALIR", dangerButtonStyle))
        {
            currentScreen = GameScreen.Lobby;
        }
    }

    private void StartLocalGame()
    {
        playerCount = Mathf.Clamp(playerCount, MinimumPlayers, MaximumPlayers);
        players.Clear();
        clues.Clear();
        for (int i = 0; i < playerCount; i++)
        {
            players.Add(new Player(PlayerNames[i], i == 0));
            players[i].SecretNumber = random.Next(MinimumNumber, MaximumNumber + 1);
        }
        activePlayerIndex = 0;
        selectedGuess = 0;
        guessesRemaining = MaximumGuesses;
        resultMessage = string.Empty;
        currentScreen = GameScreen.Game;
        roundNumber = 1;
        turnStartedAt = Time.time;
        AddBotClues();
        botTurnAt = Time.time + BotTurnDelay;
    }

    private void SubmitClue()
    {
        if (currentScreen != GameScreen.Game ||
            players.Count == 0 ||
            !players[activePlayerIndex].IsLocal ||
            string.IsNullOrWhiteSpace(clueText))
        {
            return;
        }

        string sanitizedClue = clueText.Trim();
        if (sanitizedClue.Length > MaximumClueLength)
        {
            sanitizedClue = sanitizedClue.Substring(0, MaximumClueLength);
        }

        clues.Add("Tú: Es un 10, pero " + sanitizedClue);
        clueText = string.Empty;
    }

    private void MakeGuess()
    {
        if (currentScreen != GameScreen.Game ||
            players.Count == 0 ||
            selectedGuess < MinimumNumber ||
            selectedGuess > MaximumNumber)
        {
            return;
        }

        if (!players[activePlayerIndex].IsLocal)
        {
            return;
        }

        guessesRemaining--;
        if (selectedGuess == players[activePlayerIndex].SecretNumber || guessesRemaining <= 0)
        {
            ResolveGuess(selectedGuess);
        }
        else
        {
            resultMessage = "NO ES " + selectedGuess + "\nTE QUEDAN " + guessesRemaining + " INTENTOS";
            resultUntil = Time.time + 1.3f;
            selectedGuess = 0;
        }
    }

    private void ResolveGuess(int guess)
    {
        if (currentScreen != GameScreen.Game ||
            players.Count < MinimumPlayers ||
            activePlayerIndex < 0 ||
            activePlayerIndex >= players.Count ||
            guess < MinimumNumber ||
            guess > MaximumNumber)
        {
            return;
        }

        Player active = players[activePlayerIndex];
        int difference = Mathf.Abs(active.SecretNumber - guess);
        int points = difference == 0 ? 3 : difference == 1 ? 1 : 0;
        active.Score += points;
        resultMessage = "TU CARTA ERA...  " + active.SecretNumber + "\n\n" +
                        "TU RESPUESTA:  " + guess + "\n\n" +
                        (points == 3 ? "¡CORRECTO!\n+3 PUNTOS" : points == 1 ? "CASI...\n+1 PUNTO" : "NO ERA ESA\n+0 PUNTOS");
        resultUntil = Time.time + 2.5f;

        bool completedRound = activePlayerIndex == players.Count - 1;
        activePlayerIndex = (activePlayerIndex + 1) % players.Count;
        if (completedRound)
        {
            roundNumber++;
        }
        selectedGuess = 0;
        guessesRemaining = MaximumGuesses;
        clues.Clear();
        AddBotClues();
        turnStartedAt = Time.time;
        botTurnAt = Time.time + BotTurnDelay;
    }

    private void SkipTurn()
    {
        if (currentScreen != GameScreen.Game ||
            players.Count < MinimumPlayers ||
            activePlayerIndex < 0 ||
            activePlayerIndex >= players.Count)
        {
            return;
        }

        Player skippedPlayer = players[activePlayerIndex];
        resultMessage = skippedPlayer.Name + " agotó el tiempo.\nTURNO OMITIDO\n+0 PUNTOS";
        resultUntil = Time.time + 2.5f;

        bool completedRound = activePlayerIndex == players.Count - 1;
        activePlayerIndex = (activePlayerIndex + 1) % players.Count;
        if (completedRound)
        {
            roundNumber++;
        }

        selectedGuess = 0;
        clues.Clear();
        AddBotClues();
        turnStartedAt = Time.time;
        botTurnAt = Time.time + BotTurnDelay;
    }

    private void AddBotClues()
    {
        Player active = players[activePlayerIndex];
        for (int i = 0; i < players.Count; i++)
        {
            if (i != activePlayerIndex)
            {
                clues.Add(players[i].Name + ": Es un 10, pero " + ClueEndings[(active.SecretNumber + i) % ClueEndings.Length]);
            }
        }
    }

    private Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private Texture2D MakeEllipseTexture(int width, int height, Color color)
    {
        Texture2D texture = new Texture2D(width, height);
        Vector2 center = new Vector2(width * 0.5f, height * 0.5f);
        float radiusX = width * 0.5f;
        float radiusY = height * 0.5f;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float dx = (x - center.x) / radiusX;
                float dy = (y - center.y) / radiusY;
                float distance = dx * dx + dy * dy;
                texture.SetPixel(x, y, distance <= 1f ? color : Color.clear);
            }
        }
        texture.Apply();
        return texture;
    }

    private Texture2D MakeRoundedTexture(int width, int height, Color color)
    {
        Texture2D texture = new Texture2D(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int edgeX = Mathf.Min(x, width - x - 1);
                int edgeY = Mathf.Min(y, height - y - 1);
                bool roundedCorner = edgeX < 14 && edgeY < 14 &&
                                     (edgeX - 14) * (edgeX - 14) + (edgeY - 14) * (edgeY - 14) > 14 * 14;
                texture.SetPixel(x, y, roundedCorner ? Color.clear : color);
            }
        }
        texture.Apply();
        return texture;
    }

    private Color Hex(string value)
    {
        Color color;
        return ColorUtility.TryParseHtmlString(value, out color) ? color : Color.magenta;
    }
}
