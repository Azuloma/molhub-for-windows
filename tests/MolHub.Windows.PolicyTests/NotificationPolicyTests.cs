using System.Text;
using System.Text.Json;
using MolHub.Windows;

internal static class NotificationPolicyTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Notifications: " + message);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static string Item(long id, string kind = "reservation_started", string projectId = "p1", string extra = "")
    {
        var commit = kind == "commit_published" ? ",\"commit\":{\"id\":\"c1\",\"title\":\"T\",\"version\":\"1.0\"}" : "";
        return $"{{\"id\":{id},\"kind\":\"{kind}\",\"projectId\":\"{projectId}\",\"projectName\":\" Proj \",\"actor\":{{\"id\":\"u1\",\"username\":\"bob\"}},\"holder\":null{commit},\"createdAt\":\"2026-09-30T01:02:03Z\"{extra}}}";
    }

    private static string Meta(long cursor, bool hasMore = false, bool reset = false, string pollAfter = "15") =>
        $"{{\"cursor\":{cursor},\"hasMore\":{(hasMore ? "true" : "false")},\"reset\":{(reset ? "true" : "false")},\"pollAfter\":{pollAfter}}}";

    private static NotificationPage? Page(string dataJson, string metaJson) => NotificationPayloadParser.Parse(Json(dataJson), Json(metaJson));

    private static NotificationPage MakePage(IEnumerable<long> ids, long cursor, bool hasMore = false, bool reset = false) =>
        Page("[" + string.Join(",", ids.Select(id => Item(id))) + "]", Meta(cursor, hasMore, reset))!;

    private static BridgeResult Failure(int status, string code, string? details = null) =>
        new("r1", status, false, null, null, code, details is null ? null : Json(details), false, BridgeOutcome.None);

    public static void Run(string sourceRoot)
    {
        // Allowlist.
        Check(BridgePolicy.IsKnownCommand("notifications") && !BridgePolicy.IsWrite("notifications"), "notifications must be a known read command, not a write.");

        // Parser: each kind.
        foreach (var kind in new[] { "reservation_started", "reservation_cancelled", "reservation_released", "commit_published" })
        {
            var page = Page("[" + Item(5, kind) + "]", Meta(5));
            Check(page is { Items.Count: 1 } && page.Items[0].Id == 5 && page.Items[0].ProjectName == "Proj" && page.Items[0].Actor?.Username == "bob"
                && page.Items[0].Holder is null && page.Items[0].CreatedAt is not null, "valid " + kind + " item must parse.");
            Check((page!.Items[0].Commit is not null) == (kind == "commit_published"), "commit is kept only for commit_published.");
        }
        Check(Page("[" + Item(1, "something_new") + "," + Item(2) + "]", Meta(2))?.Items.Select(i => i.Id).SequenceEqual([2L]) == true, "unknown kinds must be skipped.");
        Check(Page("[" + Item(0) + "," + Item(-3) + "," + Item(4, projectId: "bad id!") + "," + Item(6) + "]", Meta(6))?.Items.Select(i => i.Id).SequenceEqual([6L]) == true, "bad id / projectId must be skipped.");
        Check(Page("[{\"id\":\"7\",\"kind\":\"reservation_started\",\"projectId\":\"p\",\"projectName\":\"n\"}," + Item(8) + "]", Meta(8))?.Items.Count == 1, "string id must be skipped without throwing.");
        Check(Page("[{\"id\":9,\"kind\":\"commit_published\",\"projectId\":\"p\",\"projectName\":\"n\"}]", Meta(9))?.Items.Count == 0, "commit_published without a commit must be skipped.");
        Check(Page("[" + string.Join(",", Enumerable.Range(1, 101).Select(i => Item(i))) + "]", Meta(101)) is null, "more than 100 items must fail.");
        Check(Page("[" + string.Join(",", Enumerable.Range(1, 100).Select(i => Item(i))) + "]", Meta(100))?.Items.Count == 100, "exactly 100 items must parse.");
        Check(Page("{}", Meta(1)) is null && NotificationPayloadParser.Parse(null, Json(Meta(1))) is null, "non-array data must fail.");
        Check(Page("[]", "{\"hasMore\":false,\"reset\":false}") is null && Page("[]", "[]") is null && NotificationPayloadParser.Parse(Json("[]"), null) is null, "meta without cursor or not an object must fail.");
        Check(Page("[]", "{\"cursor\":\"5\",\"hasMore\":false,\"reset\":false}") is null && Page("[]", "{\"cursor\":-1,\"hasMore\":false,\"reset\":false}") is null, "cursor must be a non-negative number.");
        Check(Page("[]", "{\"cursor\":1,\"hasMore\":\"no\",\"reset\":false}") is null, "hasMore must be a boolean.");
        Check(Page("[]", Meta(1, pollAfter: "1"))!.Meta.PollAfterSeconds == 15 && Page("[]", Meta(1, pollAfter: "9999"))!.Meta.PollAfterSeconds == 300
            && Page("[]", Meta(1, pollAfter: "60"))!.Meta.PollAfterSeconds == 60 && Page("[]", "{\"cursor\":1,\"hasMore\":false,\"reset\":false}")!.Meta.PollAfterSeconds == 15
            && Page("[]", Meta(1, pollAfter: "\"x\""))!.Meta.PollAfterSeconds == 15, "pollAfter must clamp to 15..300 and default to 15.");
        Check(Page("[" + Item(1, extra: "") + "]", Meta(1))!.Items[0].CreatedAt == DateTimeOffset.Parse("2026-09-30T01:02:03Z"), "createdAt must parse as ISO.");
        Check(Page("[" + Item(1).Replace("2026-09-30T01:02:03Z", "garbage") + "]", Meta(1))!.Items[0].CreatedAt is null, "bad createdAt must become null.");

        // Feed: first load.
        var feed = new NotificationFeed();
        feed.Initialize(null);
        var payload = feed.BuildPayload();
        Check(payload.Count == 1 && payload["limit"]!.GetValue<int>() == 100, "first payload is only the limit.");
        var result = feed.ApplyFirstLoad(MakePage([3, 4, 5], 5));
        Check(feed.LastSeen == 5 && feed.UnreadCount == 0 && result.LastSeenChanged && result.NewItems.Count == 0 && feed.Cursor == 5
            && feed.Items.Select(i => i.Id).SequenceEqual([5L, 4L, 3L]), "first run for an account: nothing unread, last-seen = cursor, newest first.");
        payload = feed.BuildPayload();
        Check(payload.Count == 2 && payload["after"]!.GetValue<long>() == 5 && payload["limit"]!.GetValue<int>() == 100, "poll payload carries after and limit.");

        feed.Initialize(3);
        result = feed.ApplyFirstLoad(MakePage([3, 4, 5], 5));
        Check(feed.UnreadCount == 2 && !feed.UnreadOverflow && !result.LastSeenChanged && feed.LastSeen == 3, "stored last-seen yields unread items.");

        feed.Initialize(0);
        feed.ApplyFirstLoad(MakePage(Enumerable.Range(1, 100).Select(i => (long)i), 100));
        Check(feed.UnreadCount == 100 && feed.UnreadOverflow, "100 items all unread must set overflow.");
        feed.Initialize(1);
        feed.ApplyFirstLoad(MakePage(Enumerable.Range(1, 100).Select(i => (long)i), 100));
        Check(feed.UnreadOverflow == false && feed.UnreadCount == 99, "an already-seen oldest item means no overflow.");
        feed.Initialize(0);
        feed.ApplyFirstLoad(MakePage(Enumerable.Range(1, 99).Select(i => (long)i), 99));
        Check(!feed.UnreadOverflow && feed.UnreadCount == 99, "fewer than 100 items never overflow.");

        feed.Initialize(500);
        result = feed.ApplyFirstLoad(MakePage([3, 4], 10));
        Check(feed.LastSeen == 10 && result.LastSeenChanged && feed.UnreadCount == 0, "stored last-seen above the cursor is clamped.");

        // Feed: polling.
        feed.Initialize(5);
        feed.ApplyFirstLoad(MakePage([4, 5], 5));
        result = feed.ApplyPoll(MakePage([7, 6, 5], 7, hasMore: true));
        Check(result.NewItems.Select(i => i.Id).SequenceEqual([6L, 7L]) && result.FetchImmediately && !result.Reset && feed.UnreadCount == 2 && feed.Cursor == 7
            && feed.Items.Select(i => i.Id).SequenceEqual([7L, 6L, 5L, 4L]), "poll adds unread items, returns new ones ascending and dedupes.");
        result = feed.ApplyPoll(MakePage([6, 7], 7));
        Check(result.NewItems.Count == 0 && feed.UnreadCount == 2 && !result.FetchImmediately && feed.Items.Count == 4, "duplicates must be ignored.");
        result = feed.ApplyPoll(MakePage([], 2, reset: true));
        Check(result.Reset && result.NewItems.Count == 0 && feed.UnreadCount == 2 && feed.Cursor == 2 && feed.LastSeen == 2 && result.LastSeenChanged
            && !result.FetchImmediately, "reset adopts the cursor, clamps last-seen, adds nothing and never toasts.");

        // Mark seen.
        feed.Initialize(5);
        feed.ApplyFirstLoad(MakePage([4, 5, 6, 7], 7));
        Check(feed.UnreadCount == 2, "setup: two unread.");
        Check(feed.MarkAllSeen() == 7 && feed.UnreadCount == 0 && !feed.UnreadOverflow && feed.LastSeen == 7, "MarkAllSeen clears and returns the value to persist.");
        Check(feed.MarkAllSeen() is null, "MarkAllSeen with nothing new persists nothing.");

        // Cap.
        feed.Initialize(0);
        feed.ApplyFirstLoad(MakePage([], 0));
        feed.ApplyPoll(MakePage(Enumerable.Range(1, 100).Select(i => (long)i), 100));
        feed.ApplyPoll(MakePage([101, 102, 103], 103));
        Check(feed.Items.Count == 100 && feed.Items[0].Id == 103 && feed.Items[^1].Id == 4, "the list is capped at 100 keeping the newest.");

        // Next step decisions.
        var ok = new BridgeResult("r", 200, true, null, null, null, null, false, BridgeOutcome.None);
        Check(NotificationFeed.Next(ok, new NotificationMeta(1, false, false, 15)) == new NotificationNextStep(NotificationNextKind.PollAfter, 15)
            && NotificationFeed.Next(ok, new NotificationMeta(1, false, false, 40)) == new NotificationNextStep(NotificationNextKind.PollAfter, 40)
            && NotificationFeed.Next(ok, new NotificationMeta(1, false, false, 1)).Seconds >= 15, "success waits at least 15 s.");
        Check(NotificationFeed.Next(ok, new NotificationMeta(1, true, false, 15)).Kind == NotificationNextKind.Immediate, "hasMore fetches immediately.");
        Check(NotificationFeed.Next(ok, null).Kind == NotificationNextKind.Backoff, "an unparsable success backs off.");
        Check(NotificationFeed.Next(Failure(429, "RATE_LIMIT", "{\"retryAfter\":120}"), null) == new NotificationNextStep(NotificationNextKind.RetryAfter, 120)
            && NotificationFeed.Next(Failure(429, "RATE_LIMIT", "{\"retryAfter\":1}"), null).Seconds == 15
            && NotificationFeed.Next(Failure(429, "RATE_LIMIT", "{\"retryAfter\":99999}"), null).Seconds == 900
            && NotificationFeed.Next(Failure(429, "RATE_LIMIT", "{\"retryAfter\":\"x\"}"), null).Seconds == 60
            && NotificationFeed.Next(Failure(429, "RATE_LIMIT"), null).Seconds == 60, "429 uses a clamped details.retryAfter with a 60 s default.");
        Check(NotificationFeed.Next(Failure(503, "MAINTENANCE"), null).Kind == NotificationNextKind.Pause, "503 pauses.");
        Check(NotificationFeed.Next(Failure(403, "NOT_APPROVED"), null).Kind == NotificationNextKind.Stop
            && NotificationFeed.Next(Failure(401, "UNAUTHORIZED"), null).Kind == NotificationNextKind.Stop, "401/403 stop.");
        Check(NotificationFeed.Next(Failure(500, "SERVER_ERROR"), null) == new NotificationNextStep(NotificationNextKind.Backoff, 60)
            && NotificationFeed.Next(BridgeResult.HostFailure("r", "TIMEOUT", true, BridgeOutcome.None), null).Kind == NotificationNextKind.Backoff, "other failures back off 60 s.");

        // Last-seen store policy.
        Check(NotificationLastSeenPolicy.KeyFor("u_1-A") == "notifications.lastSeen.u_1-A" && NotificationLastSeenPolicy.KeyFor(null) is null
            && NotificationLastSeenPolicy.KeyFor("") is null && NotificationLastSeenPolicy.KeyFor("a.b") is null && NotificationLastSeenPolicy.KeyFor("a b") is null
            && NotificationLastSeenPolicy.KeyFor(new string('a', 65)) is null, "KeyFor rejects invalid ids.");
        Check(NotificationLastSeenPolicy.ParseStored("123") == 123 && NotificationLastSeenPolicy.ParseStored(45L) == 45L && NotificationLastSeenPolicy.ParseStored("0") == 0
            && NotificationLastSeenPolicy.ParseStored("abc") is null && NotificationLastSeenPolicy.ParseStored("-1") is null && NotificationLastSeenPolicy.ParseStored(-1L) is null
            && NotificationLastSeenPolicy.ParseStored("1234567890123456") is null && NotificationLastSeenPolicy.ParseStored("") is null
            && NotificationLastSeenPolicy.ParseStored(" 1") is null && NotificationLastSeenPolicy.ParseStored(null) is null && NotificationLastSeenPolicy.ParseStored(1.5) is null,
            "ParseStored accepts 1-15 digits or a non-negative Int64 only.");
        Check(NotificationLastSeenPolicy.Format(42) == "42" && NotificationLastSeenPolicy.ParseStored(NotificationLastSeenPolicy.Format(987654321012345)) == 987654321012345, "Format round-trips.");

        // AuthenticatedUser id.
        static AuthenticatedUser? User(string userJson) =>
            ProfilePayloadParser.ParseAuthenticatedUser(200, Encoding.UTF8.GetBytes("{\"user\":{" + userJson + "\"username\":\"x\",\"status\":\"approved\",\"role\":\"member\"}}"));
        Check(User("\"id\":\"user_1-A\",")?.Id == "user_1-A", "a valid user id must be kept.");
        Check(User("")?.Id is null && User("\"id\":\"bad id\",")?.Id is null && User("\"id\":12,")?.Id is null && User("\"id\":null,")?.Id is null
            && User("\"id\":\"" + new string('a', 65) + "\",")?.Username == "x", "a missing or invalid id yields null and never rejects the sign-in.");

        // Notification text (step 2): a fake localizer returns the English formats.
        var formats = new Dictionary<string, string>
        {
            ["Notifications_ReservationStarted"] = "{0} started working on {1}",
            ["Notifications_ReservationCancelled"] = "{0} cancelled their work reservation on {1}",
            ["Notifications_ReservationReleased"] = "{0} released {1}'s work reservation on {2}",
            ["Notifications_ReservationReleasedSystem"] = "{0}'s work reservation on {1} was released automatically",
            ["Notifications_CommitPublished"] = "{0} published a commit to {1}",
            ["Notifications_CommitLineFormat"] = "{0} · {1}",
            ["Notifications_UnknownUser"] = "Unknown user"
        };
        string Localize(string key) => formats.TryGetValue(key, out var value) ? value : key;
        static NotificationItem Make(NotificationKind kind, string? actor, string? holder, bool hasActor = true, bool hasHolder = true, NotificationCommit? commit = null) =>
            new(1, kind, "p1", "Proj", hasActor ? new NotificationUser("u1", actor) : null, hasHolder ? new NotificationUser("u2", holder) : null, commit, null);
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationStarted, "bob", null, hasHolder: false), Localize) == "bob started working on Proj", "started sentence.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationCancelled, "bob", "bob"), Localize) == "bob cancelled their work reservation on Proj", "cancelled sentence.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationReleased, "amy", "bob"), Localize) == "amy released bob's work reservation on Proj", "released sentence.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationReleased, null, "bob", hasActor: false), Localize) == "bob's work reservation on Proj was released automatically", "a released item without an actor uses the system sentence.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationReleased, "amy", null), Localize) == "amy released Unknown user's work reservation on Proj", "a null holder name uses the unknown-user text.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationStarted, null, null, hasHolder: false), Localize) == "Unknown user started working on Proj", "a null username uses the unknown-user text.");
        Check(NotificationText.Sentence(Make(NotificationKind.ReservationStarted, null, null, hasActor: false, hasHolder: false), Localize) == "Unknown user started working on Proj", "a null actor uses the unknown-user text.");
        var commitItem = Make(NotificationKind.CommitPublished, "bob", null, hasHolder: false, commit: new NotificationCommit("c1", "Fix bug", "1.2"));
        Check(NotificationText.Sentence(commitItem, Localize) == "bob published a commit to Proj", "commit sentence.");
        Check(NotificationText.CommitLine(commitItem, Localize) == "Fix bug · 1.2", "commit line is title and version.");
        Check(NotificationText.CommitLine(Make(NotificationKind.ReservationStarted, "bob", null), Localize) is null, "only commits have a second line.");

        // Step 2 wiring (source assertions).
        var pollerCode = File.ReadAllText(Path.Combine(sourceRoot, "Notifications", "NotificationPoller.cs"));
        var mainCode = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "MainWindow.xaml.cs"));
        var signOutCode = File.ReadAllText(Path.Combine(sourceRoot, "Shell", "SignOutCoordinator.cs"));
        var centerCode = File.ReadAllText(Path.Combine(sourceRoot, "Notifications", "NotificationCenter.cs"));
        Check(pollerCode.Contains("generation != _generation", StringComparison.Ordinal) && pollerCode.Contains("_generation++", StringComparison.Ordinal)
            && pollerCode.Contains("NotificationFeed.Next(", StringComparison.Ordinal) && pollerCode.Contains("NotificationLastSeenStore.Write", StringComparison.Ordinal),
            "the poller must ignore results of an old generation, decide delays with NotificationFeed.Next and persist last-seen.");
        var signOutStart = signOutCode.IndexOf("public async Task SignOutAsync", StringComparison.Ordinal);
        var signOutBody = signOutCode[signOutStart..];
        var stopIndex = signOutBody.IndexOf("_syncNotifications();", StringComparison.Ordinal);
        var logoutIndex = signOutBody.IndexOf("_bridge.RequestAsync(\"logout\")", StringComparison.Ordinal);
        Check(signOutStart >= 0 && stopIndex >= 0 && logoutIndex > stopIndex && signOutBody.IndexOf("_writeGate.Close();", StringComparison.Ordinal) < stopIndex,
            "sign-out must stop the notification poller before the bridge logout.");
        Check(centerCode.Contains("var shouldRun = !_isSigningOut()", StringComparison.Ordinal) && centerCode.Contains("_notifications.Stop();", StringComparison.Ordinal)
            && signOutCode[signOutCode.IndexOf("public void SignOutAbandoned", StringComparison.Ordinal)..].Contains("_syncNotifications();", StringComparison.Ordinal)
            && mainCode.Contains("            SyncNotifications,", StringComparison.Ordinal),
            "the poller must stop when signing out and restart after an abandoned sign-out.");
        Check(!mainCode.Contains("ReferenceEquals(sender", StringComparison.Ordinal) && !pollerCode.Contains("ReferenceEquals(sender", StringComparison.Ordinal),
            "no ReferenceEquals(sender) checks.");
        var textCode = File.ReadAllText(Path.Combine(sourceRoot, "Notifications", "NotificationText.cs"));
        Check(textCode.Contains("Notifications_ReservationReleasedSystem", StringComparison.Ordinal) && textCode.Contains("Notifications_UnknownUser", StringComparison.Ordinal)
            && textCode.Contains("item.Actor is null", StringComparison.Ordinal), "NotificationText must handle a null actor for released items and a null username.");
        var enResw = File.ReadAllText(Path.Combine(sourceRoot, "Strings", "en-US", "Resources.resw"));
        var jaResw = File.ReadAllText(Path.Combine(sourceRoot, "Strings", "ja-JP", "Resources.resw"));
        foreach (var key in new[] { "Notifications_ReservationStarted", "Notifications_ReservationCancelled", "Notifications_ReservationReleased", "Notifications_ReservationReleasedSystem",
            "Notifications_CommitPublished", "Notifications_CommitLineFormat", "Notifications_UnknownUser", "Notifications_Empty", "Notifications_Loading", "Notifications_Paused",
            "Notifications_Error", "Notifications_UnreadFormat", "Notifications_UnreadItem" })
        {
            Check(enResw.Contains("<data name=\"" + key + "\">", StringComparison.Ordinal) && jaResw.Contains("<data name=\"" + key + "\">", StringComparison.Ordinal), key + " must exist in both resw files.");
        }

        // Step 3: toasts.
        NotificationItem ToastItem(long id, NotificationKind kind = NotificationKind.ReservationStarted, string projectId = "p1") =>
            new(id, kind, projectId, "Proj", new NotificationUser("u1", "bob"), null,
                kind == NotificationKind.CommitPublished ? new NotificationCommit("c1", "T", "1.0") : null, null);
        IReadOnlyList<NotificationItem> Items(int n) => Enumerable.Range(1, n).Select(i => ToastItem(i)).ToList();
        Check(NotificationToastPolicy.SummaryThreshold == 4, "summary threshold is 4.");
        Check(NotificationToastPolicy.Plan(Items(2), windowActive: false, enabled: false).Kind == ToastPlanKind.None, "no toasts when disabled.");
        Check(NotificationToastPolicy.Plan(Items(2), windowActive: true, enabled: true).Kind == ToastPlanKind.None, "no toasts while the window is active.");
        Check(NotificationToastPolicy.Plan(Items(0), windowActive: false, enabled: true).Kind == ToastPlanKind.None, "no toasts for an empty batch.");
        foreach (var n in new[] { 1, 2, 3 })
        {
            var plan = NotificationToastPolicy.Plan(Items(n), false, true);
            Check(plan.Kind == ToastPlanKind.PerItem && plan.Items.Count == n, "1-3 items give one toast each.");
        }
        foreach (var n in new[] { 4, 5, 50 })
        {
            var plan = NotificationToastPolicy.Plan(Items(n), false, true);
            Check(plan.Kind == ToastPlanKind.Summary && plan.Items.Count == n, "4+ items give one summary.");
        }
        Dictionary<string, string> ToDict(IEnumerable<KeyValuePair<string, string>> pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);
        var openTarget = NotificationToastPolicy.ParseArguments(ToDict(NotificationToastPolicy.BuildItemArguments(ToastItem(1))));
        Check(openTarget is { Action: ToastAction.Open, ProjectId: "p1", CommitId: null }, "open round trip without a commit.");
        var commitArgs = ToDict(NotificationToastPolicy.BuildItemArguments(ToastItem(2, NotificationKind.CommitPublished)));
        Check(commitArgs.Keys.OrderBy(k => k).SequenceEqual(["action", "commitId", "projectId"]), "arguments carry only action, projectId and commitId.");
        Check(NotificationToastPolicy.ParseArguments(commitArgs) is { Action: ToastAction.Open, ProjectId: "p1", CommitId: "c1" }, "open round trip with a commit.");
        Check(NotificationToastPolicy.ParseArguments(ToDict(NotificationToastPolicy.BuildListArguments())) is { Action: ToastAction.List, ProjectId: null }, "list round trip.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "list", ["extra"] = "x" }) is { Action: ToastAction.List }, "unknown keys are ignored.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "open", ["projectId"] = "p1", ["extra"] = "x" }) is { ProjectId: "p1" }, "unknown keys are ignored for open.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "open", ["projectId"] = "bad id!" }) is null, "bad projectId rejected.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "open", ["projectId"] = new string('a', 65) }) is null, "long projectId rejected.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "open", ["projectId"] = "p1", ["commitId"] = "../x" }) is null, "bad commitId rejected.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "open" }) is null, "open without projectId rejected.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["action"] = "delete", ["projectId"] = "p1" }) is null, "unknown action rejected.");
        Check(NotificationToastPolicy.ParseArguments(new Dictionary<string, string> { ["projectId"] = "p1" }) is null, "missing action rejected.");

        var manifest = File.ReadAllText(Path.Combine(sourceRoot, "Package.appxmanifest"));
        const string clsid = "25795B1A-2F3B-4348-80FC-017460C00443";
        Check(manifest.Contains("windows.toastNotificationActivation", StringComparison.Ordinal) && manifest.Contains("ToastActivatorCLSID=\"" + clsid + "\"", StringComparison.Ordinal)
            && manifest.Contains("windows.comServer", StringComparison.Ordinal) && manifest.Contains("<com:Class Id=\"" + clsid + "\"", StringComparison.Ordinal), "manifest must declare toast activation and the COM server class with the same CLSID.");
        var appCode = File.ReadAllText(Path.Combine(sourceRoot, "App.xaml.cs"));
        Check(appCode.Contains("manager.Register()", StringComparison.Ordinal) && appCode.Contains("AppNotificationManager.Default.Unregister()", StringComparison.Ordinal)
            && appCode.Contains("NotificationInvoked +=", StringComparison.Ordinal), "App must register and unregister AppNotificationManager.");
        var toastSignOutStart = signOutCode.IndexOf("public async Task SignOutAsync", StringComparison.Ordinal);
        var toastSignOut = signOutCode[toastSignOutStart..];
        var toastStop = toastSignOut.IndexOf("_syncNotifications();", StringComparison.Ordinal);
        var toastRemove = toastSignOut.IndexOf("_clearToasts();", StringComparison.Ordinal);
        var toastLogout = toastSignOut.IndexOf("_bridge.RequestAsync(\"logout\")", StringComparison.Ordinal);
        Check(toastStop >= 0 && toastRemove > toastStop && toastLogout > toastRemove && centerCode.Contains("NotificationToasts.RemoveAllAsync();", StringComparison.Ordinal)
            && mainCode.Contains("() => _notificationCenter?.ClearToasts());", StringComparison.Ordinal), "sign-out must remove toasts after the poller stop and before logout.");
        Check(mainCode.Contains("private void SyncNotifications() => _notificationCenter.SyncNotifications();", StringComparison.Ordinal), "MainWindow.SyncNotifications must forward to the notification center.");
        var syncBody = centerCode[centerCode.IndexOf("public void SyncNotifications()", StringComparison.Ordinal)..];
        syncBody = syncBody[..syncBody.IndexOf("_notificationsRunning) return;", StringComparison.Ordinal)];
        Check(syncBody.Contains("!_isSignOutRequested()", StringComparison.Ordinal), "SyncNotifications must require !_isSignOutRequested() (the MainWindow _signOutRequested flag).");
        var abandoned = signOutCode[signOutCode.IndexOf("public void SignOutAbandoned", StringComparison.Ordinal)..];
        Check(abandoned.IndexOf("_signOutRequested = false;", StringComparison.Ordinal) is var clear and >= 0 && clear < abandoned.IndexOf("_syncNotifications();", StringComparison.Ordinal),
            "_signOutRequested must be cleared only in SignOutAbandoned, before SyncNotifications.");
        Check(signOutCode.Split("_signOutRequested = false;").Length == 2 && !mainCode.Contains("_signOutRequested", StringComparison.Ordinal), "_signOutRequested must be cleared in one place.");
        var toastsCode = File.ReadAllText(Path.Combine(sourceRoot, "Notifications", "NotificationToasts.cs"));
        Check(centerCode.Contains("_notifications.ItemsArrived += Notifications_ItemsArrived;", StringComparison.Ordinal)
            && centerCode.Split("NotificationToasts.Show(").Length == 2 && !mainCode.Contains("NotificationToasts", StringComparison.Ordinal) && !pollerCode.Contains("NotificationToasts", StringComparison.Ordinal)
            && toastsCode.Contains("SetTag(", StringComparison.Ordinal), "toasts must be shown only from the ItemsArrived subscription.");
        var enResw2 = enResw.Contains("Notifications_ToastSummaryFormat", StringComparison.Ordinal) && jaResw.Contains("Notifications_ToastSummaryFormat", StringComparison.Ordinal);
        Check(enResw2, "the toast summary string must exist in both resw files.");

        // Source hygiene.
        foreach (var file in Directory.GetFiles(Path.Combine(sourceRoot, "Notifications"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "Debug.", "Trace.", "Console.", "Cookie", "Headers" })
            {
                Check(!text.Contains(forbidden, StringComparison.Ordinal), Path.GetFileName(file) + " must not contain " + forbidden);
            }
        }
    }
}
