namespace TeamsAgent.Core;
public record RecordingOperation(string Location, string Status);
public interface IRecordingStatusTransport { Task<RecordingOperation> RequestAsync(string callId, bool recording, CancellationToken ct); Task<RecordingOperation> PollAsync(RecordingOperation operation, CancellationToken ct); }
public sealed class RecordingConfirmation(IRecordingStatusTransport transport)
{
    public async Task StartAsync(string callId, RecordingGate gate, CancellationToken ct) { gate.Revoke(); await SetAsync(callId, true, ct); ct.ThrowIfCancellationRequested(); gate.Confirm(); }
    public Task StopAsync(string callId, CancellationToken ct) => SetAsync(callId, false, ct);
    async Task SetAsync(string callId, bool recording, CancellationToken ct) { using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(30)); var op = await transport.RequestAsync(callId, recording, deadline.Token); while (op.Status != "completed") { deadline.Token.ThrowIfCancellationRequested(); if (op.Status == "failed") throw new InvalidOperationException("Opptaksstatus ble ikke bekreftet."); if (string.IsNullOrEmpty(op.Location)) throw new InvalidOperationException("Opptaksoperasjonen kan ikke spores."); op = await transport.PollAsync(op, deadline.Token); if (op.Status != "completed" && op.Status != "failed") await Task.Delay(100, deadline.Token); } deadline.Token.ThrowIfCancellationRequested(); }
}
public static class Attendance { public static bool ActuallyAttended(bool? inLobby, bool isApplication) => inLobby == false && !isApplication; }
public static class SessionCallbacks { public static MeetingSession Existing(MeetingSession? current, DateTimeOffset now) { var s = current ?? throw new KeyNotFoundException("Møtet er slettet."); if (Retention.IsExpired(s.Created, now)) throw new InvalidOperationException("Møtet er utløpt."); return s; } }
