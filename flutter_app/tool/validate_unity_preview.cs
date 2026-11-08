var bootstrap = UnityEngine.Object.FindFirstObjectByType<EasyARCloudBlockBootstrap>();
var bridge = UnityEngine.Object.FindFirstObjectByType<DataPerson.Host.UnityFlutterBridge>();
if (!bootstrap || !bootstrap.PreviewMode || !bootstrap.DigitalHuman || !bridge)
    throw new System.Exception("Run Play Flutter Offline Preview before this check.");
var actor = bootstrap.DigitalHuman;
var actorId = actor.GetInstanceID();
bridge.ReceiveCommand("{\"version\":1,\"type\":\"OpenPlace\",\"mode\":\"preview\",\"requestId\":\"editor-check\"}");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"SetConversationState\",\"value\":\"Listening\"}");
if (actor.StateMachine.ConversationState != DataPerson.DigitalHuman.AvatarConversationState.Listening)
    throw new System.Exception("Conversation bridge failed.");
bootstrap.SetPreviewTracking(false);
if (actor.gameObject.activeSelf) throw new System.Exception("Lost did not hide the avatar.");
bootstrap.SetPreviewTracking(true);
if (!actor.gameObject.activeSelf || bootstrap.DigitalHuman.GetInstanceID() != actorId)
    throw new System.Exception("Found did not restore the same avatar.");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"PauseAR\"}");
if (!bootstrap.SessionPaused || !UnityEngine.AudioListener.pause) throw new System.Exception("Pause failed.");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"ResumeAR\"}");
if (bootstrap.SessionPaused || UnityEngine.AudioListener.pause) throw new System.Exception("Resume failed.");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"CloseAR\"}");
if (!bootstrap.SessionPaused) throw new System.Exception("Close failed.");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"OpenPlace\",\"mode\":\"preview\"}");
if (bootstrap.SessionPaused) throw new System.Exception("Reopen failed.");
bridge.ReceiveCommand("{\"version\":1,\"type\":\"SetConversationState\",\"value\":\"Idle\"}");
var accepted = actor.PlayGesture(DataPerson.DigitalHuman.AvatarGesture.Greeting);
if (!accepted) throw new System.Exception("Wave gesture failed.");
var sessions = UnityEngine.Object.FindObjectsByType<easyar.ARSession>(UnityEngine.FindObjectsSortMode.None).Length;
if (sessions != 0) throw new System.Exception("Preview unexpectedly created an AR session.");
var output = System.IO.Path.Combine(System.IO.Directory.GetParent(UnityEngine.Application.dataPath).FullName,
    "flutter_app/validation");
System.IO.Directory.CreateDirectory(output);
var result = new {
    validatedAtUtc = System.DateTime.UtcNow.ToString("O"),
    preview = bootstrap.PreviewMode,
    actor = actor.name,
    sameInstanceAfterLostFound = true,
    conversationBridge = true,
    pauseResumeAndReopen = true,
    waveAccepted = accepted,
    arSessionCount = sessions,
    androidRuntimeVerified = false
};
System.IO.File.WriteAllText(System.IO.Path.Combine(output, "unity-preview-check.json"),
    Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented));
UnityEngine.ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(output, "unity-preview.png"));
return result;
