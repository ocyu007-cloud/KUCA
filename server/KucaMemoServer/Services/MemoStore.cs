using KucaMemoServer.Models;
using Microsoft.Data.Sqlite;

namespace KucaMemoServer.Services;

/// <summary>
/// 메모와 메모의 좋아요·댓글을 SQLite 파일(기본: memos.db)에 저장한다. 서버를 껐다 켜도 남는다.
/// 파일 위치는 설정 "Memos:DatabasePath" 로 바꿀 수 있다 (테스트에서 임시 파일을 쓸 때).
/// </summary>
public class MemoStore
{
    readonly string connectionString;

    public MemoStore(IConfiguration config, IWebHostEnvironment env)
    {
        string path = config["Memos:DatabasePath"] ?? Path.Combine(env.ContentRootPath, "memos.db");
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        CreateTable();
    }

    void CreateTable()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        // created_at 은 UTC Ticks(정수)로 저장해 정렬과 "이전 메모" 비교를 정확하게 한다.
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS memos (
                id          TEXT PRIMARY KEY,
                building_id TEXT NOT NULL,
                author      TEXT NOT NULL,
                text        TEXT NOT NULL,
                photo_url   TEXT,
                device_id   TEXT NOT NULL,
                created_at  INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS memos_building_created ON memos (building_id, created_at DESC);

            -- 좋아요: 한 기기는 한 메모에 한 번만 (기본 키로 중복을 막는다)
            CREATE TABLE IF NOT EXISTS memo_likes (
                memo_id    TEXT NOT NULL,
                device_id  TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                PRIMARY KEY (memo_id, device_id)
            );

            CREATE TABLE IF NOT EXISTS memo_comments (
                id         TEXT PRIMARY KEY,
                memo_id    TEXT NOT NULL,
                author     TEXT NOT NULL,
                text       TEXT NOT NULL,
                device_id  TEXT NOT NULL,
                created_at INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS memo_comments_memo_created ON memo_comments (memo_id, created_at);
            """;
        cmd.ExecuteNonQuery();

        // 내용 검사(C-2): 공개 상태와 검토 메모. 예전 memos.db 에는 열이 없으므로 없을 때만 더한다.
        // status: 'visible'(모두에게 보임) / 'pending'(검토 대기, 작성 기기에만 보임)
        foreach (string table in new[] { "memos", "memo_comments" })
        {
            AddColumnIfMissing(conn, table, "status", "TEXT NOT NULL DEFAULT 'visible'");
            AddColumnIfMissing(conn, table, "flag_note", "TEXT");
        }
    }

    static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        using var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column";
        check.Parameters.AddWithValue("$column", column);
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            return;

        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    SqliteConnection Open()
    {
        var conn = new SqliteConnection(connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// 메모 열 + 좋아요 수·공개 댓글 수·$viewer 기기가 좋아요를 눌렀는지.
    /// $viewer 가 NULL 이면 liked_by_me 는 항상 0.
    /// </summary>
    const string MemoColumns = """
        SELECT m.id, m.building_id, m.author, m.text, m.photo_url, m.device_id, m.created_at,
               (SELECT COUNT(*) FROM memo_likes l WHERE l.memo_id = m.id) AS like_count,
               (SELECT COUNT(*) FROM memo_comments c WHERE c.memo_id = m.id AND c.status = 'visible') AS comment_count,
               EXISTS (SELECT 1 FROM memo_likes l WHERE l.memo_id = m.id AND l.device_id = $viewer) AS liked_by_me,
               m.status, m.flag_note
        """;

    /// <summary>
    /// 건물의 메모를 최신순으로. before 가 있으면 그 시각보다 이전 것만.
    /// viewer 는 likedByMe 를 계산할 기기 ID 이고, 그 기기가 쓴 검토 대기 메모도 함께 돌려준다.
    /// </summary>
    public List<Memo> List(string buildingId, int limit, DateTime? before, string? viewer = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $$"""
            {{MemoColumns}}
            FROM memos m
            WHERE m.building_id = $building AND ($before IS NULL OR m.created_at < $before)
              AND (m.status = 'visible' OR m.device_id = $viewer)
            ORDER BY m.created_at DESC, m.id DESC
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$building", buildingId);
        cmd.Parameters.AddWithValue("$before", before.HasValue ? before.Value.ToUniversalTime().Ticks : DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$viewer", (object?)viewer ?? DBNull.Value);
        return ReadAll(cmd);
    }

    /// <summary>메모 하나 (공개 상태와 상관없이). 보여 줘도 되는지는 호출하는 쪽이 IsVisibleTo 로 판단한다.</summary>
    public Memo? Find(string id, string? viewer = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $$"""
            {{MemoColumns}}
            FROM memos m WHERE m.id = $id
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$viewer", (object?)viewer ?? DBNull.Value);
        return ReadAll(cmd).FirstOrDefault();
    }

    public void Add(Memo memo)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memos (id, building_id, author, text, photo_url, device_id, created_at, status, flag_note)
            VALUES ($id, $building, $author, $text, $photo, $device, $created, $status, $note)
            """;
        cmd.Parameters.AddWithValue("$id", memo.Id);
        cmd.Parameters.AddWithValue("$building", memo.BuildingId);
        cmd.Parameters.AddWithValue("$author", memo.Author);
        cmd.Parameters.AddWithValue("$text", memo.Text);
        cmd.Parameters.AddWithValue("$photo", (object?)memo.PhotoUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$device", memo.DeviceId);
        cmd.Parameters.AddWithValue("$created", memo.CreatedAt.ToUniversalTime().Ticks);
        cmd.Parameters.AddWithValue("$status", memo.Status);
        cmd.Parameters.AddWithValue("$note", (object?)memo.FlagNote ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>메모를 지운다. 그 메모의 좋아요·댓글도 함께 지운다.</summary>
    public bool Delete(string id)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.CommandText = "DELETE FROM memo_likes WHERE memo_id = $id; DELETE FROM memo_comments WHERE memo_id = $id;";
        cmd.ExecuteNonQuery();
        cmd.CommandText = "DELETE FROM memos WHERE id = $id";
        bool deleted = cmd.ExecuteNonQuery() > 0;
        tx.Commit();
        return deleted;
    }

    // ---------- 좋아요 ----------

    /// <summary>좋아요를 누르거나(liked=true) 취소한다. 여러 번 해도 결과가 같다. 바뀐 뒤의 좋아요 수를 돌려준다.</summary>
    public int SetLike(string memoId, string deviceId, bool liked)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = liked
            ? "INSERT OR IGNORE INTO memo_likes (memo_id, device_id, created_at) VALUES ($memo, $device, $now)"
            : "DELETE FROM memo_likes WHERE memo_id = $memo AND device_id = $device";
        cmd.Parameters.AddWithValue("$memo", memoId);
        cmd.Parameters.AddWithValue("$device", deviceId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
        cmd.ExecuteNonQuery();

        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT COUNT(*) FROM memo_likes WHERE memo_id = $memo";
        cmd.Parameters.AddWithValue("$memo", memoId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ---------- 댓글 ----------

    const string CommentColumns = "SELECT id, memo_id, author, text, device_id, created_at, status, flag_note";

    /// <summary>
    /// 메모의 댓글을 오래된 순으로. after 가 있으면 그 시각보다 나중 것만.
    /// viewer 기기가 쓴 검토 대기 댓글도 함께 돌려준다.
    /// </summary>
    public List<Comment> ListComments(string memoId, int limit, DateTime? after, string? viewer = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $$"""
            {{CommentColumns}}
            FROM memo_comments
            WHERE memo_id = $memo AND ($after IS NULL OR created_at > $after)
              AND (status = 'visible' OR device_id = $viewer)
            ORDER BY created_at, id
            LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$memo", memoId);
        cmd.Parameters.AddWithValue("$after", after.HasValue ? after.Value.ToUniversalTime().Ticks : DBNull.Value);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$viewer", (object?)viewer ?? DBNull.Value);
        return ReadComments(cmd);
    }

    public Comment? FindComment(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{CommentColumns} FROM memo_comments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return ReadComments(cmd).FirstOrDefault();
    }

    public void AddComment(Comment comment)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO memo_comments (id, memo_id, author, text, device_id, created_at, status, flag_note)
            VALUES ($id, $memo, $author, $text, $device, $created, $status, $note)
            """;
        cmd.Parameters.AddWithValue("$id", comment.Id);
        cmd.Parameters.AddWithValue("$memo", comment.MemoId);
        cmd.Parameters.AddWithValue("$author", comment.Author);
        cmd.Parameters.AddWithValue("$text", comment.Text);
        cmd.Parameters.AddWithValue("$device", comment.DeviceId);
        cmd.Parameters.AddWithValue("$created", comment.CreatedAt.ToUniversalTime().Ticks);
        cmd.Parameters.AddWithValue("$status", comment.Status);
        cmd.Parameters.AddWithValue("$note", (object?)comment.FlagNote ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool DeleteComment(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM memo_comments WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    // ---------- 검토 대기 (관리자) ----------

    /// <summary>검토 대기 메모, 오래된 순. 관리자용이라 flagNote 를 채운다.</summary>
    public List<Memo> ListPendingMemos()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $$"""
            {{MemoColumns}}
            FROM memos m WHERE m.status = 'pending'
            ORDER BY m.created_at, m.id
            """;
        cmd.Parameters.AddWithValue("$viewer", DBNull.Value);
        return ReadAll(cmd, includeNote: true);
    }

    /// <summary>검토 대기 댓글, 오래된 순. 관리자용이라 flagNote 를 채운다.</summary>
    public List<Comment> ListPendingComments()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"{CommentColumns} FROM memo_comments WHERE status = 'pending' ORDER BY created_at, id";
        return ReadComments(cmd, includeNote: true);
    }

    /// <summary>메모를 공개한다. 없으면 false.</summary>
    public bool ApproveMemo(string id) => Approve("memos", id);

    /// <summary>댓글을 공개한다. 없으면 false.</summary>
    public bool ApproveComment(string id) => Approve("memo_comments", id);

    bool Approve(string table, string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE {table} SET status = 'visible', flag_note = NULL WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>공개된 메모가 있는 건물별 메모 수 (웹 페이지용)</summary>
    public Dictionary<string, int> CountByBuilding()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT building_id, COUNT(*) FROM memos WHERE status = 'visible' GROUP BY building_id";
        using var reader = cmd.ExecuteReader();
        var counts = new Dictionary<string, int>();
        while (reader.Read())
            counts[reader.GetString(0)] = reader.GetInt32(1);
        return counts;
    }

    static List<Memo> ReadAll(SqliteCommand cmd, bool includeNote = false)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<Memo>();
        while (reader.Read())
        {
            list.Add(new Memo
            {
                Id = reader.GetString(0),
                BuildingId = reader.GetString(1),
                Author = reader.GetString(2),
                Text = reader.GetString(3),
                PhotoUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                DeviceId = reader.GetString(5),
                CreatedAt = new DateTime(reader.GetInt64(6), DateTimeKind.Utc),
                LikeCount = reader.GetInt32(7),
                CommentCount = reader.GetInt32(8),
                LikedByMe = reader.GetInt64(9) != 0,
                Status = reader.GetString(10),
                FlagNote = includeNote && !reader.IsDBNull(11) ? reader.GetString(11) : null,
            });
        }
        return list;
    }

    static List<Comment> ReadComments(SqliteCommand cmd, bool includeNote = false)
    {
        using var reader = cmd.ExecuteReader();
        var list = new List<Comment>();
        while (reader.Read())
        {
            list.Add(new Comment
            {
                Id = reader.GetString(0),
                MemoId = reader.GetString(1),
                Author = reader.GetString(2),
                Text = reader.GetString(3),
                DeviceId = reader.GetString(4),
                CreatedAt = new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
                Status = reader.GetString(6),
                FlagNote = includeNote && !reader.IsDBNull(7) ? reader.GetString(7) : null,
            });
        }
        return list;
    }
}
