using System;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Vortice.Mathematics;

class GameMain : G2AppBase
{
    public override System.Drawing.Size ScreenSize => GameGlobal.ScreenSize;
    public override string GameName => GameGlobal.GameName;

    private G2Texture _Wallpaper = null;
    private G2Texture _hg = null;
    private G2Texture _Start = null;
    private G2Texture _Snakemein = null;
    private G2Texture _Gameend = null;
    private G2Texture _Pool = null;

    private G2Texture _headUp = null;
    private G2Texture _headDown = null;
    private G2Texture _headLeft = null;
    private G2Texture _headRight = null;
    private G2Texture _apple = null;

    private readonly Dictionary<string, G2Texture> snakeTextures =
        new Dictionary<string, G2Texture>();

    private G2AudioMp3? bgm;
    private G2AudioMp3? game;
    private G2AudioSound? clickSound;

    private List<Point> snake = new List<Point>();
    private Point direction = new Point(1, 0);
    private Point food;

    // 연두색 격자
    private const int GridColumns = 17;
    private const int GridRows = 15;

    private const float BackgroundScale = 560f / 1024f;

    private const float GridLeft =
        200f + 54f * BackgroundScale;

    private const float GridTop =
        40f + 162f * BackgroundScale;

    private const float CellWidth =
        916f * BackgroundScale / GridColumns;

    private const float CellHeight =
        808f * BackgroundScale / GridRows;

    private DateTime moveTimer = DateTime.Now;
    private double moveDelay = 0.15;
    private Random _random = new Random();

    private bool _waitingForExit = false;
    private readonly Dictionary<G2Texture, Vortice.RawRectF> headSources =
    new Dictionary<G2Texture, Vortice.RawRectF>();

    private G2Texture LoadHead(string path)
    {
        var texture = new G2Texture(path);

        using (var bitmap = new System.Drawing.Bitmap(path))
        {
            int left = bitmap.Width;
            int top = bitmap.Height;
            int right = -1;
            int bottom = -1;

            // PNG에서 실제 그림이 있는 범위 찾기
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).A < 32)
                        continue;

                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }

            if (right < left || bottom < top)
            {
                throw new InvalidOperationException(
                    "머리 이미지가 비어 있습니다: " + path);
            }

            headSources[texture] = new Vortice.RawRectF(
                left, top, right + 1, bottom + 1);
        }

        return texture;
    }

    private int score = 0;
    private int bestScore = 0;

    private G2Texture _scoreTexture = null;
    private G2Texture _bestScoreTexture = null;
    private G2Texture _resultTitleTexture = null;

    private System.Drawing.Bitmap _scorePanelBitmap = null;

    private readonly string scoreSaveFolder = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "SnakeGameScores"
    );

    private readonly string scoreTempFolder = Path.Combine(
        Path.GetTempPath(),
        "SnakeScore_" + Guid.NewGuid().ToString("N")
    );

    private void InitializeScores(string uiFolder)
    {
        Directory.CreateDirectory(scoreSaveFolder);
        Directory.CreateDirectory(scoreTempFolder);

        string savePath = Path.Combine(scoreSaveFolder, "best.txt");

        if (File.Exists(savePath))
        {
            if (int.TryParse(File.ReadAllText(savePath), out int saved))
            {
                bestScore = Math.Max(0, saved);
            }
        }

        _scorePanelBitmap = new System.Drawing.Bitmap(
            Path.Combine(uiFolder, "ui_score_panel.png"));

        UpdateScoreTexture(
            ref _resultTitleTexture, "title", "게임 결과");

        RefreshScoreTextures();
    }

    private void UpdateScoreTexture(
        ref G2Texture texture,
        string fileName,
        string text)
    {
        texture?.Dispose();
        texture = null;

        string path = Path.Combine(
            scoreTempFolder, fileName + ".png");

        using (var bitmap = new System.Drawing.Bitmap(
            512,
            128,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font(
                "맑은 고딕",
                36f,
                System.Drawing.FontStyle.Bold,
                System.Drawing.GraphicsUnit.Pixel))
            using (var brush = new System.Drawing.SolidBrush(
                System.Drawing.Color.FromArgb(255, 245, 255, 211)))
            using (var format = new System.Drawing.StringFormat())
            {
                graphics.Clear(System.Drawing.Color.Transparent);

                graphics.DrawImage(
                    _scorePanelBitmap,
                    new System.Drawing.Rectangle(0, 0, 512, 128));

                graphics.TextRenderingHint =
                    System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                format.Alignment =
                    System.Drawing.StringAlignment.Center;

                format.LineAlignment =
                    System.Drawing.StringAlignment.Center;

                graphics.DrawString(
                    text,
                    font,
                    brush,
                    new System.Drawing.RectangleF(16, 8, 480, 112),
                    format);
            }

            bitmap.Save(
                path,
                System.Drawing.Imaging.ImageFormat.Png);
        }

        texture = new G2Texture(path);
    }

    private void RefreshScoreTextures()
    {
        UpdateScoreTexture(
            ref _scoreTexture,
            "current",
            $"점수 : {score}");

        UpdateScoreTexture(
            ref _bestScoreTexture,
            "best",
            $"최고 점수 : {bestScore}");
    }

    private void AddScore()
    {
        score++;

        if (score > bestScore)
        {
            bestScore = score;

            File.WriteAllText(
                Path.Combine(scoreSaveFolder, "best.txt"),
                bestScore.ToString());
        }

        RefreshScoreTextures();
    }

    private enum GameState
    {
        Start,
        Playing,
        Clear,
        GameOver
    }

    private GameState _state = GameState.Start;

    protected override void Initialize()
    {
        string imageFolder = Path.Combine(
            AppContext.BaseDirectory,
            "resource",
            "tex_play"
        );

        string uiFolder = Path.Combine(
            AppContext.BaseDirectory,
            "resource",
            "tex_ui"
        );

        _Wallpaper = new G2Texture(
            Path.Combine(imageFolder, "ui_wallpaper.png"));

        _hg = new G2Texture(
            Path.Combine(imageFolder, "ui_bg.png"));

        _Start = new G2Texture(
            Path.Combine(imageFolder, "ui_Start.png"));

        _Snakemein = new G2Texture(
            Path.Combine(imageFolder, "ui_Snake.png"));

        _Gameend = new G2Texture(
            Path.Combine(imageFolder, "ui_gameend.png"));

        _Pool = new G2Texture(
            Path.Combine(imageFolder, "ui_pool.png"));

        // 머리: 새 PNG는 모두 64 × 64
        _headUp = new G2Texture(
        Path.Combine(uiFolder, "ui_head_up_v3.png"));

        _headDown = new G2Texture(
            Path.Combine(uiFolder, "ui_head_b_v3.png"));

        _headLeft = new G2Texture(
            Path.Combine(uiFolder, "ui_head_L_v3.png"));

        _headRight = new G2Texture(
            Path.Combine(uiFolder, "ui_head_R_v3.png"));

        // 몸통, 꼬리, 모서리
        _headUp = new G2Texture(
            Path.Combine(uiFolder, "ui_head_up_v3.png"));

        _headDown = new G2Texture(
            Path.Combine(uiFolder, "ui_head_b_v3.png"));

        _headLeft = new G2Texture(
            Path.Combine(uiFolder, "ui_head_L_v3.png"));

        _headRight = new G2Texture(
            Path.Combine(uiFolder, "ui_head_R_v3.png"));

        string[] snakeParts =
        {
            "ui_body",
            "ui_body_v",
            "ui_tail",
            "ui_tail_L",
            "ui_tail_up",
            "ui_tail_b",
            "ui_corner_rd",
            "ui_corner_dl",
            "ui_corner_lu",
            "ui_corner_ur"
};
        foreach (string part in snakeParts)
        {
            snakeTextures.Add(
                part,
                new G2Texture(
                    Path.Combine(uiFolder, part + "_v3.png"))
            );
        }

        // 사과는 기존 이미지 사용
        _apple = new G2Texture(
            Path.Combine(uiFolder, "ui_apple.png"));

        string audioFolder = Path.Combine(
            AppContext.BaseDirectory,
            "resource",
            "audio"
        );

        bgm = new G2AudioMp3(
            Path.Combine(audioFolder, "bgm.mp3"));

        clickSound = new G2AudioSound(
            Path.Combine(audioFolder, "click.wav"));

        game = new G2AudioMp3(
            Path.Combine(audioFolder, "game.mp3"));

        InitializeScores(uiFolder);

        bgm.Play(true);
        bgm.Play(true);
    }

    // 격자 한 칸의 화면 위치
    private Vortice.RawRectF GetCellRect(Point cell)
    {
        float left = GridLeft + cell.X * CellWidth;
        float top = GridTop + cell.Y * CellHeight;

        return new Vortice.RawRectF(
            left,
            top,
            left + CellWidth,
            top + CellHeight
        );
    }

    private void SpawnFood()
    {
        // 빈칸에서만 사과 생성
        var emptyCells = new List<Point>();

        for (int y = 0; y < GridRows; y++)
        {
            for (int x = 0; x < GridColumns; x++)
            {
                Point cell = new Point(x, y);

                if (!snake.Contains(cell))
                {
                    emptyCells.Add(cell);
                }
            }
        }

        if (emptyCells.Count == 0)
        {
            _state = GameState.Clear;
            game?.Stop();
            bgm?.Play(true);
            return;
        }

        food = emptyCells[_random.Next(emptyCells.Count)];
    }

    private void EndGame()
    {
        _state = GameState.GameOver;
        game?.Stop();
        bgm?.Play(true);
    }

    protected override void Update()
    {
        double elapsed = TotalTime;

        ClearColor = new Color4(
            red: (float)(Math.Sin(elapsed) * 0.5 + 0.5),
            green: (float)(Math.Sin(elapsed + Math.PI / 2.0) * 0.5 + 0.5),
            blue: (float)(Math.Sin(elapsed + Math.PI) * 0.5 + 0.5),
            alpha: 1.0f
        );

        if (_waitingForExit)
        {
            if (clickSound == null || !clickSound.IsPlaying())
            {
                Close();
            }

            return;
        }

        if (_state == GameState.Start)
        {
            if (Input.IsButtonDown(MouseButtons.Left))
            {
                var mouse = Input.MousePosition;

                var startButton =
                    new RectangleF(324, 340, 312, 130);

                var exitButton =
                    new RectangleF(324, 489, 312, 130);

                if (startButton.Contains(mouse))
                {
                    _state = GameState.Playing;

                    clickSound?.Play();
                    game?.Play(true);
                    bgm?.Stop();
                    score = 0;

                    RefreshScoreTextures();

                    snake.Clear();
                    snake.Clear();
                    snake.Add(new Point(10, 7));
                    snake.Add(new Point(9, 7));
                    snake.Add(new Point(8, 7));

                    direction = new Point(1, 0);

                    SpawnFood();

                    moveTimer = DateTime.Now;
                }
                else if (exitButton.Contains(mouse))
                {
                    clickSound?.Play();
                    bgm?.Stop();

                    _waitingForExit = true;
                    return;
                }
            }
        }
        else if (_state == GameState.Playing)
        {
            // 실제로 움직인 방향을 기준으로 역방향 입력 방지
            Point currentDirection = snake.Count > 1
                ? new Point(
                    snake[0].X - snake[1].X,
                    snake[0].Y - snake[1].Y)
                : direction;

            if (Input.IsKeyDown(Keys.W) &&
                currentDirection.Y != 1)
            {
                direction = new Point(0, -1);
            }
            else if (Input.IsKeyDown(Keys.S) &&
                     currentDirection.Y != -1)
            {
                direction = new Point(0, 1);
            }
            else if (Input.IsKeyDown(Keys.A) &&
                     currentDirection.X != 1)
            {
                direction = new Point(-1, 0);
            }
            else if (Input.IsKeyDown(Keys.D) &&
                     currentDirection.X != -1)
            {
                direction = new Point(1, 0);
            }

            if ((DateTime.Now - moveTimer).TotalSeconds >= moveDelay)
            {
                moveTimer = DateTime.Now;

                Point head = snake[0];

                Point newHead = new Point(
                    head.X + direction.X,
                    head.Y + direction.Y
                );

                // 격자 바깥으로 나가면 종료
                if (newHead.X < 0 ||
                    newHead.X >= GridColumns ||
                    newHead.Y < 0 ||
                    newHead.Y >= GridRows)
                {
                    EndGame();
                    return;
                }

                bool eating = newHead == food;

                // 먹지 않는 턴에는 현재 꼬리 칸이 비워짐
                int collisionCount = eating
                    ? snake.Count
                    : snake.Count - 1;

                for (int i = 0; i < collisionCount; i++)
                {
                    if (snake[i] == newHead)
                    {
                        EndGame();
                        return;
                    }
                }

                snake.Insert(0, newHead);

                if (eating)
                {
                    AddScore();
                    SpawnFood();
                }
                else
                {
                    snake.RemoveAt(snake.Count - 1);
                }
            }
        }
        else if (_state == GameState.GameOver ||
                 _state == GameState.Clear)
        {
            if (Input.IsButtonDown(MouseButtons.Left) ||
                Input.IsKeyDown(Keys.Enter))
            {
                _state = GameState.Start;
                clickSound?.Play();
            }
        }
    }

    private void DrawSnake()
    {
        // 모든 PNG가 같은 64×64 캔버스와 연결 위치를 사용
        var source = new Vortice.RawRectF(0, 0, 64, 64);

        for (int i = 0; i < snake.Count; i++)
        {
            Point p = snake[i];
            G2Texture texture;

            if (i == 0)
            {
                Point facing = snake.Count > 1
                    ? new Point(
                        p.X - snake[1].X,
                        p.Y - snake[1].Y)
                    : direction;

                if (facing.X < 0)
                    texture = _headLeft;
                else if (facing.X > 0)
                    texture = _headRight;
                else if (facing.Y < 0)
                    texture = _headUp;
                else
                    texture = _headDown;
            }
            else if (i == snake.Count - 1)
            {
                Point previous = snake[i - 1];

                string key;

                if (p.X < previous.X)
                    key = "ui_tail_L";
                else if (p.X > previous.X)
                    key = "ui_tail";
                else if (p.Y < previous.Y)
                    key = "ui_tail_up";
                else
                    key = "ui_tail_b";

                texture = snakeTextures[key];
            }
            else
            {
                Point before = snake[i - 1];
                Point after = snake[i + 1];

                bool left =
                    before.X < p.X || after.X < p.X;

                bool right =
                    before.X > p.X || after.X > p.X;

                bool up =
                    before.Y < p.Y || after.Y < p.Y;

                bool down =
                    before.Y > p.Y || after.Y > p.Y;

                string key;

                if (left && right)
                    key = "ui_body";
                else if (up && down)
                    key = "ui_body_v";
                else if (right && down)
                    key = "ui_corner_rd";
                else if (down && left)
                    key = "ui_corner_dl";
                else if (left && up)
                    key = "ui_corner_lu";
                else
                    key = "ui_corner_ur";

                texture = snakeTextures[key];
            }

            // 부위별 확대·축소 없이 같은 격자 크기로 표시
            texture.Draw(GetCellRect(p), source);
        }
    }

    protected override void Render()
    {
        _Wallpaper.Draw(
            new Vortice.RawRectF(0, 0, 960, 640),
            new Vortice.RawRectF(0, 0, 1024, 559)
        );

        switch (_state)
        {
            case GameState.Start:
                _Snakemein.Draw(
                    new Vortice.RawRectF(142, -30, 818, 339),
                    new Vortice.RawRectF(0, 0, 676, 369)
                );

                _Start.Draw(
                    new Vortice.RawRectF(142, 220, 818, 589),
                    new Vortice.RawRectF(0, 0, 676, 369)
                );

                _Gameend.Draw(
                    new Vortice.RawRectF(142, 370, 818, 739),
                    new Vortice.RawRectF(0, 0, 1696, 928)
                );
                break;

            case GameState.Playing:
                _hg.Draw(
                    new Vortice.RawRectF(200, 40, 760, 600),
                    new Vortice.RawRectF(0, 0, 1024, 1024)
                );

                // 풀
                _Pool.Draw(
                    new Vortice.RawRectF(-10, 495, 250, 625),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                _Pool.Draw(
                    new Vortice.RawRectF(710, 495, 970, 625),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                _Pool.Draw(
                    new Vortice.RawRectF(170, 525, 370, 625),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                _Pool.Draw(
                    new Vortice.RawRectF(590, 525, 790, 625),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                _Pool.Draw(
                    new Vortice.RawRectF(40, 475, 360, 635),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                _Pool.Draw(
                    new Vortice.RawRectF(600, 475, 920, 635),
                    new Vortice.RawRectF(0, 0, 1774, 887)
                );

                // 사과: 기존 원본의 투명 여백 제외
                _apple.Draw(
                    GetCellRect(food),
                    new Vortice.RawRectF(156, 102, 375, 350)
                );

                DrawSnake();

                _scoreTexture.Draw(
                    new Vortice.RawRectF(470, 48, 730, 106),
                    new Vortice.RawRectF(0, 0, 512, 128)
                );
                break;

            case GameState.Clear:
            case GameState.GameOver:
                // 게임 결과 제목
                _resultTitleTexture.Draw(
                    new Vortice.RawRectF(280, 160, 680, 260),
                    new Vortice.RawRectF(0, 0, 512, 128)
                );

                // 이번 게임 점수
                _scoreTexture.Draw(
                    new Vortice.RawRectF(280, 280, 680, 380),
                    new Vortice.RawRectF(0, 0, 512, 128)
                );

                // 저장된 최고 점수
                _bestScoreTexture.Draw(
                    new Vortice.RawRectF(280, 400, 680, 500),
                    new Vortice.RawRectF(0, 0, 512, 128)
                );
                break;
        }
    }

    public override void Dispose()
    {
        _Wallpaper?.Dispose();
        _hg?.Dispose();
        _Start?.Dispose();
        _Snakemein?.Dispose();
        _Gameend?.Dispose();
        _Pool?.Dispose();

        _headUp?.Dispose();
        _headDown?.Dispose();
        _headLeft?.Dispose();
        _headRight?.Dispose();
        _apple?.Dispose();

        foreach (G2Texture texture in snakeTextures.Values)
        {
            texture.Dispose();
        }

        snakeTextures.Clear();

        _scoreTexture?.Dispose();
        _bestScoreTexture?.Dispose();
        _resultTitleTexture?.Dispose();
        _scorePanelBitmap?.Dispose();

        try
        {
            if (Directory.Exists(scoreTempFolder))
            {
                Directory.Delete(scoreTempFolder, true);
            }
        }
        catch (IOException)
        {
            // 사용 중인 임시 파일은 남겨 둠
        }
        catch (UnauthorizedAccessException)
        {
            // 임시 파일 삭제 실패는 게임 종료에 영향을 주지 않음
        }
    }
}