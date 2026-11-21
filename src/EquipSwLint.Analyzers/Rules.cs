using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace EquipSwLint
{
    /// <summary>Rule metadata in one place: analyzers, CLI (SARIF/HTML) and viewer rules.json all read this.</summary>
    public sealed class RuleInfo
    {
        public RuleInfo(string id, string stage, string category, string overlap,
            string titleEn, string titleKo, string messageEn, string messageKo,
            string whyEn, string whyKo, string fixEn, string fixKo, string repro)
        {
            Id = id; Stage = stage; Category = category; Overlap = overlap;
            TitleEn = titleEn; TitleKo = titleKo; MessageEn = messageEn; MessageKo = messageKo;
            WhyEn = whyEn; WhyKo = whyKo; FixEn = fixEn; FixKo = fixKo; Repro = repro;
            Descriptor = new DiagnosticDescriptor(id, new Text(titleEn, titleKo), new Text(messageEn, messageKo), category,
                stage == "shadow" ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, isEnabledByDefault: true,
                description: new Text(whyEn + " " + fixEn, whyKo + " " + fixKo),
                helpLinkUri: "https://mhbae-semi-lab.github.io/equip-sw-lint/viewer/#rule/" + id);
        }

        public string Id { get; }
        /// <summary>"enforce" (Warning) or "shadow" (Info: reported, never breaks the build until promoted in .editorconfig).</summary>
        public string Stage { get; }
        public string Category { get; }
        /// <summary>Other analyzers that already cover the same pattern (empty = none found in the 2026-11 survey).</summary>
        public string Overlap { get; }
        public string TitleEn { get; }
        public string TitleKo { get; }
        public string MessageEn { get; }
        public string MessageKo { get; }
        public string WhyEn { get; }
        public string WhyKo { get; }
        public string FixEn { get; }
        public string FixKo { get; }
        /// <summary>xUnit test that reproduces the failure at run time.</summary>
        public string Repro { get; }
        public DiagnosticDescriptor Descriptor { get; }
    }

    /// <summary>English/Korean text picked by the UI culture of the build, IDE or CLI.</summary>
    internal sealed class Text : LocalizableString
    {
        private readonly string _en, _ko;
        public Text(string en, string ko) { _en = en; _ko = ko; }
        protected override string GetText(IFormatProvider? formatProvider)
        {
            // same fallback as LocalizableResourceString (ResourceManager uses the UI culture when none is given)
#pragma warning disable RS1035
            var culture = formatProvider as CultureInfo ?? CultureInfo.CurrentUICulture;
#pragma warning restore RS1035
            return culture.TwoLetterISOLanguageName == "ko" ? _ko : _en;
        }
        protected override int GetHash() => _en.GetHashCode();
        protected override bool AreEqual(object? other) => other is Text t && t._en == _en && t._ko == _ko;
    }

    public static class Rules
    {
        private const string Time = "Time", Threading = "Threading", Io = "IO", Memory = "Memory";

        public static readonly RuleInfo TickCount32 = new RuleInfo("ESL001", "enforce", Time, "",
            "32-bit tick counter wraps after 24.9 days",
            "32bit 틱 카운터는 24.9일 뒤 넘어간다",
            "'{0}' wraps negative after 24.9 days of PC uptime (and repeats every 49.7 days); this comparison, widening or division gives wrong results after the wrap. Use Environment.TickCount64 or Stopwatch.",
            "'{0}' 는 PC 가동 24.9일 뒤 음수로 넘어가고 49.7일마다 반복된다. 이 비교 · 형 넓힘 · 나눗셈은 넘어간 뒤 틀린 값을 낸다. Environment.TickCount64 나 Stopwatch 를 쓴다.",
            "Equipment PCs run for weeks. A deadline 'TickCount + timeout' or a comparison 'TickCount > deadline' flips sign at the wrap: timeouts fire at once or never, watchdogs stop, and the bug appears only after ~25 days in the field.",
            "장비 PC 는 몇 주씩 켜져 있다. 'TickCount + timeout' 으로 만든 기한이나 'TickCount > deadline' 비교는 넘어가는 순간 부호가 뒤집혀 타임아웃이 바로 나거나 영영 나지 않고, 감시 타이머가 멈춘다. 현장에서 약 25일이 지나야 드러난다.",
            "Use Environment.TickCount64 (.NET Core 3.0+) or Stopwatch.GetTimestamp/Elapsed. If you must keep int ticks, only compare differences: unchecked(Environment.TickCount - start) > timeout.",
            "Environment.TickCount64 (.NET Core 3.0 이상) 나 Stopwatch 를 쓴다. int 틱을 꼭 써야 하면 차이만 비교한다: unchecked(Environment.TickCount - start) > timeout.",
            "Repro_ESL001_DeadlineBreaksAtWrap");

        public static readonly RuleInfo WallClockInterval = new RuleInfo("ESL002", "enforce", Time, "SonarQube S6561",
            "Interval measured with the wall clock",
            "벽시계(DateTime.Now)로 잰 간격",
            "Interval or timeout measured with DateTime.{0}: the wall clock jumps when the PC time is synced or changed, so this elapsed time can go negative or huge. Use Stopwatch or Environment.TickCount64.",
            "DateTime.{0} 로 간격이나 타임아웃을 잰다. PC 시각을 맞추거나 바꾸면 벽시계가 건너뛰어 경과 시간이 음수나 매우 큰 값이 된다. Stopwatch 나 Environment.TickCount64 를 쓴다.",
            "Equipment PCs sync their clock with the host/MES or NTP. A jump of minutes makes 'DateTime.Now - start' negative (timeout never fires) or huge (instant timeout alarm), and DST changes do the same with local time.",
            "장비 PC 는 호스트 · MES · NTP 로 시각을 맞춘다. 몇 분이 건너뛰면 'DateTime.Now - start' 가 음수(타임아웃이 안 남)나 큰 값(바로 타임아웃 알람)이 된다. 서머타임 전환도 같다.",
            "Measure with Stopwatch.StartNew()/Elapsed or Environment.TickCount64 differences. Keep DateTime.Now for timestamps shown to people and for file retention dates.",
            "Stopwatch.StartNew()/Elapsed 나 Environment.TickCount64 차이로 잰다. DateTime.Now 는 사람에게 보이는 시각과 파일 보관 기한에만 쓴다.",
            "Repro_ESL002_ClockJumpBreaksTimeout");

        public static readonly RuleInfo TimerReentrancy = new RuleInfo("ESL003", "enforce", Threading, "",
            "Periodic timer callback can overlap itself",
            "주기 타이머 콜백이 겹쳐 실행된다",
            "{0} fires every period even if the previous callback is still running; '{1}' does blocking work with no reentrancy guard, so callbacks overlap when the device is slow. Make it one-shot and re-arm at the end, or skip while busy (Interlocked).",
            "{0} 는 앞 콜백이 끝나지 않아도 주기마다 다시 실행한다. '{1}' 는 오래 걸리는 작업을 하면서 재진입 막기가 없어, 장비 응답이 느려지면 콜백이 겹친다. 한 번만 실행하고 끝에서 다시 거는 방식이나, 실행 중이면 건너뛰기(Interlocked)를 쓴다.",
            "System.Threading.Timer and System.Timers.Timer (AutoReset) run callbacks on the thread pool. When a serial query or file write takes longer than the period, a second callback starts on another thread: two threads talk to the same port, replies get mixed, and the pool fills up until the whole program stalls.",
            "System.Threading.Timer 와 System.Timers.Timer(AutoReset) 는 스레드 풀에서 콜백을 실행한다. 시리얼 조회나 파일 쓰기가 주기보다 오래 걸리면 두 번째 콜백이 다른 스레드에서 시작된다. 두 스레드가 같은 포트에 말을 걸어 응답이 섞이고, 풀이 차면 프로그램 전체가 멈춘다.",
            "Create the timer one-shot (period Timeout.Infinite / AutoReset = false) and re-arm it in a finally block at the end of the callback, or guard with 'if (Interlocked.Exchange(ref _busy, 1) == 1) return;' ... 'finally { _busy = 0; }'.",
            "타이머를 한 번만 실행하게 만들고(period Timeout.Infinite 또는 AutoReset = false) 콜백 끝 finally 에서 다시 건다. 또는 'if (Interlocked.Exchange(ref _busy, 1) == 1) return;' ... 'finally { _busy = 0; }' 로 막는다.",
            "Repro_ESL003_SlowCallbackOverlaps");

        public static readonly RuleInfo ReadWithoutTimeout = new RuleInfo("ESL004", "enforce", Io, "",
            "Serial/socket read with no timeout",
            "타임아웃 없는 시리얼 · 소켓 읽기",
            "'{0}' blocks forever when the device stops answering: {1}. Set a read timeout or pass a CancellationToken with a deadline.",
            "'{0}' 는 장비가 응답을 멈추면 영원히 기다린다: {1}. 읽기 타임아웃을 정하거나 기한이 있는 CancellationToken 을 넘긴다.",
            "SerialPort.ReadTimeout, Socket.ReceiveTimeout and NetworkStream.ReadTimeout default to infinite, and timeouts never apply to ReadAsync. A cable pull, a controller reboot or a half-open TCP link leaves the reading thread stuck with no alarm; the sequence waits forever.",
            "SerialPort.ReadTimeout, Socket.ReceiveTimeout, NetworkStream.ReadTimeout 기본값은 무한이고, ReadAsync 에는 타임아웃이 아예 적용되지 않는다. 케이블이 빠지거나 컨트롤러가 재부팅하거나 TCP 연결이 반쯤 끊기면 읽는 스레드가 알람 없이 멈추고 시퀀스는 계속 기다린다.",
            "Set port.ReadTimeout / client.ReceiveTimeout (ms) and handle TimeoutException/IOException as a communication alarm. For async reads pass a token from 'new CancellationTokenSource(timeout)'.",
            "port.ReadTimeout / client.ReceiveTimeout(ms)을 정하고 TimeoutException · IOException 을 통신 알람으로 처리한다. 비동기 읽기는 'new CancellationTokenSource(timeout)' 의 토큰을 넘긴다.",
            "Repro_ESL004_SilentDeviceHangsRead");

        public static readonly RuleInfo UntimedWait = new RuleInfo("ESL005", "shadow", Threading, "",
            "Wait with no timeout",
            "타임아웃 없는 대기",
            "'{0}' without a timeout waits forever if the signal never comes (device hang, missed event, stuck thread). Pass a timeout and handle the timeout path.",
            "타임아웃 없는 '{0}' 는 신호가 끝내 오지 않으면(장비 멈춤, 놓친 이벤트, 멈춘 스레드) 영원히 기다린다. 타임아웃을 넘기고 시간 초과 경로를 처리한다.",
            "WaitOne(), Join(), Monitor.Wait(), SemaphoreSlim.Wait() and Process.WaitForExit() with no timeout turn one lost signal into a frozen sequence or a program that never exits on shutdown. Task.Wait/.Result are left to VSTHRD002 / CA rules.",
            "타임아웃 없는 WaitOne(), Join(), Monitor.Wait(), SemaphoreSlim.Wait(), Process.WaitForExit() 는 신호 하나를 놓치면 시퀀스가 멈추거나 종료할 때 프로그램이 끝나지 않는다. Task.Wait/.Result 는 VSTHRD002 · CA 규칙이 맡는다.",
            "Use the timeout overload (WaitOne(ms), Join(ms), Wait(ms)) and raise an alarm or retry when it returns false.",
            "타임아웃이 있는 오버로드(WaitOne(ms), Join(ms), Wait(ms))를 쓰고 false 가 오면 알람이나 재시도를 한다.",
            "Repro_ESL005_LostSignalBlocksForever");

        public static readonly RuleInfo NonAtomicWrite = new RuleInfo("ESL006", "shadow", Io, "",
            "Settings/recipe file overwritten in place",
            "설정 · 레시피 파일을 제자리에 덮어쓴다",
            "'{0}' overwrites '{1}' in place: a crash, power loss or kill during the write leaves a truncated file that fails to load at the next start. Write to a temp file in the same folder, then File.Replace/File.Move over the original.",
            "'{0}' 는 '{1}' 를 제자리에 덮어쓴다. 쓰는 도중 프로그램이 죽거나 전원이 나가면 잘린 파일이 남아 다음 시작에서 읽지 못한다. 같은 폴더의 임시 파일에 쓴 뒤 File.Replace / File.Move 로 바꾼다.",
            "File.WriteAllText, StreamWriter(path), FileMode.Create and XDocument.Save(path) truncate the file first and write it afterwards. Equipment is powered off at the switch and programs are killed by watchdogs, so a half-written recipe or config is a real failure: the tool will not start, or starts with defaults.",
            "File.WriteAllText, StreamWriter(path), FileMode.Create, XDocument.Save(path) 는 파일을 먼저 비우고 나중에 쓴다. 장비는 스위치로 전원을 끄고 감시 프로그램이 프로세스를 죽이기도 해서, 반만 쓴 레시피 · 설정은 실제로 생긴다. 프로그램이 시작하지 않거나 기본값으로 시작한다.",
            "Write to 'path + \".tmp\"' (same folder), flush (FileStream.Flush(true)), then File.Replace(tmp, path, backup) or File.Move(tmp, path, overwrite: true).",
            "같은 폴더의 'path + \".tmp\"' 에 쓰고 flush(FileStream.Flush(true)) 한 뒤 File.Replace(tmp, path, backup) 나 File.Move(tmp, path, overwrite: true) 로 바꾼다.",
            "Repro_ESL006_CrashMidWriteCorruptsRecipe");

        public static readonly RuleInfo UnlockedSharedBuffer = new RuleInfo("ESL007", "shadow", Threading, "PVS-Studio V3223, Infer# RacerD (general data races)",
            "Buffer shared by callback and UI without a lock",
            "콜백과 UI 가 락 없이 공유하는 버퍼",
            "Buffer field '{0}' is written on a background/callback thread ('{1}') and used by UI code ('{2}') with no lock: the UI can read a half-written frame or a resized array. Lock both sides or hand over a finished copy.",
            "버퍼 필드 '{0}' 를 백그라운드 · 콜백 스레드('{1}')가 쓰고 UI 코드('{2}')가 락 없이 쓴다. UI 가 반만 쓴 프레임이나 크기가 바뀐 배열을 읽을 수 있다. 양쪽에 락을 걸거나 다 만든 복사본을 넘긴다.",
            "Camera/grabber callbacks fill image buffers on their own thread while the UI paints from the same array or Bitmap. Without a lock the screen shows two frames mixed, and a buffer replaced mid-read throws IndexOutOfRange or 'object is in use elsewhere' and takes the vision program down.",
            "카메라 · 그래버 콜백은 자기 스레드에서 영상 버퍼를 채우고, UI 는 같은 배열이나 Bitmap 으로 화면을 그린다. 락이 없으면 화면에 두 프레임이 섞이고, 읽는 도중 버퍼가 바뀌면 IndexOutOfRange 나 'object is in use elsewhere' 로 비전 프로그램이 죽는다.",
            "Take the same lock in the callback and in the UI, or build the frame in a private buffer and publish it with Interlocked.Exchange (double buffering).",
            "콜백과 UI 에서 같은 락을 잡거나, 개인 버퍼에 프레임을 다 만든 뒤 Interlocked.Exchange 로 넘긴다(더블 버퍼).",
            "Repro_ESL007_TornFrameWithoutLock");

        public static readonly RuleInfo UnboundedQueue = new RuleInfo("ESL008", "enforce", Memory, "",
            "Queue fed by a callback has no size limit",
            "콜백이 채우는 큐에 크기 제한이 없다",
            "'{0}' grows without limit: '{1}' adds from a device/timer callback with no size check. When processing is slower than the camera or sensor for hours, memory climbs until the process dies. Bound it or check Count and drop.",
            "'{0}' 는 끝없이 커진다: '{1}' 가 장비 · 타이머 콜백에서 크기 확인 없이 넣는다. 처리가 카메라 · 센서보다 몇 시간 느리면 메모리가 계속 늘어 프로세스가 죽는다. 크기를 제한하거나 Count 를 보고 버린다.",
            "A frame queue that grows by 1% of the frame rate looks fine in a 10-minute test and runs out of memory after a day. ConcurrentQueue, Queue, BlockingCollection without capacity and unbounded channels never push back on the producer.",
            "프레임 속도의 1% 씩 늘어나는 큐는 10분 시험에서는 멀쩡하고 하루 뒤 메모리가 넘친다. ConcurrentQueue, Queue, 용량 없는 BlockingCollection, 제한 없는 Channel 은 생산자를 멈추지 않는다.",
            "Use new BlockingCollection<T>(capacity) with TryAdd (drop and count), Channel.CreateBounded with BoundedChannelFullMode.DropOldest, or check Count before Enqueue and record dropped frames.",
            "new BlockingCollection<T>(capacity) 와 TryAdd(버리고 개수 기록), BoundedChannelFullMode.DropOldest 를 쓴 Channel.CreateBounded, 또는 Enqueue 전에 Count 를 보고 버린 프레임을 기록한다.",
            "Repro_ESL008_SlowConsumerGrowsQueue");

        public static readonly RuleInfo InvokeUnderLock = new RuleInfo("ESL009", "enforce", Threading, "Klocwork CS.BANNED.INVOKE (bans all synchronous Control.Invoke)",
            "Synchronous UI call while holding a lock",
            "락을 잡은 채 동기 UI 호출",
            "Synchronous '{0}' inside lock({1}): it waits for the UI thread while holding the lock. If any UI handler takes the same lock or waits for this thread, both stop and the window freezes. Copy data under the lock and call BeginInvoke after releasing it.",
            "lock({1}) 안의 동기 '{0}' 는 락을 잡은 채 UI 스레드를 기다린다. UI 처리기가 같은 락을 잡거나 이 스레드를 기다리면 둘 다 멈추고 창이 얼어붙는다. 락 안에서는 데이터만 복사하고, 락을 푼 뒤 BeginInvoke 를 부른다.",
            "Control.Invoke, Dispatcher.Invoke and SendMessage block until the UI thread runs the call. If the UI thread is waiting for the same lock (a paint or button handler reading shared state), neither side moves: the classic window-message deadlock behind many 'Vision Down' reports.",
            "Control.Invoke, Dispatcher.Invoke, SendMessage 는 UI 스레드가 실행할 때까지 막힌다. UI 스레드가 같은 락을 기다리면(공유 상태를 읽는 그리기 · 버튼 처리기) 어느 쪽도 못 움직인다. 많은 'Vision Down' 의 원인인 창 메시지 교착이다.",
            "Inside the lock only copy what the UI needs; after the lock, call BeginInvoke/InvokeAsync (or Dispatcher.BeginInvoke) with the copy.",
            "락 안에서는 UI 에 필요한 값만 복사하고, 락 밖에서 그 복사본으로 BeginInvoke/InvokeAsync(또는 Dispatcher.BeginInvoke)를 부른다.",
            "Repro_ESL009_InvokeUnderLockDeadlocks");

        public static readonly RuleInfo StartBeforeReady = new RuleInfo("ESL010", "shadow", Threading, "",
            "Field initialized after receiving already started",
            "수신을 시작한 뒤에 필드를 초기화한다",
            "Field '{0}' is set after '{1}' already started '{2}', which uses it. If the first message, frame or tick arrives in between, '{2}' sees null or old state. Initialize everything the handler needs before starting.",
            "'{1}' 로 '{2}' 가 이미 시작된 뒤에 그 처리기가 쓰는 필드 '{0}' 를 설정한다. 그 사이에 첫 메시지 · 프레임 · 틱이 오면 '{2}' 는 null 이나 이전 상태를 본다. 처리기에 필요한 것을 모두 만든 뒤에 시작한다.",
            "Opening a port, starting a thread or a timer begins delivering callbacks immediately. When the queue or state they use is assigned a line later, a restart that happens to receive the first message early drops it or throws NullReferenceException: a race that shows up as 'sometimes stuck after restart'.",
            "포트를 열거나 스레드 · 타이머를 시작하면 바로 콜백이 오기 시작한다. 콜백이 쓰는 큐나 상태를 한 줄 뒤에 만들면, 첫 메시지가 일찍 온 재시작에서 메시지를 버리거나 NullReferenceException 이 난다. '재시작하면 가끔 멈춘다'로 보이는 레이스다.",
            "Move the assignment above the Open/Start call (create queues, buffers and state first, start receiving last).",
            "대입을 Open/Start 호출 위로 옮긴다(큐 · 버퍼 · 상태를 먼저 만들고 수신은 마지막에 시작).",
            "Repro_ESL010_EarlyMessageLostOnRestart");

        public static readonly RuleInfo PollWithoutDeadline = new RuleInfo("ESL011", "shadow", Threading, "PVS-Studio V3120/V3032 (related, not the same)",
            "Polling loop with no deadline",
            "기한 없는 폴링 루프",
            "Polling loop waits for '{0}' with Sleep and no deadline: if the device or IO never reaches that state, this thread waits forever and the sequence hangs without an alarm. Add a timeout and raise an alarm when it expires.",
            "'{0}' 를 Sleep 으로 기다리는 폴링 루프에 기한이 없다. 장비 · IO 가 그 상태가 되지 않으면 스레드가 영원히 기다리고 시퀀스가 알람 없이 멈춘다. 타임아웃을 두고 넘으면 알람을 낸다.",
            "'while (!axis.IsDone) Thread.Sleep(10);' is fine until a sensor cable breaks or an interlock blocks the motion. Then the sequence never ends, no alarm is raised and the operator only sees a frozen screen.",
            "'while (!axis.IsDone) Thread.Sleep(10);' 는 센서 케이블이 끊기거나 인터락이 동작을 막기 전까지는 괜찮다. 그때는 시퀀스가 끝나지 않고 알람도 없이 작업자는 멈춘 화면만 본다.",
            "Track elapsed time (var sw = Stopwatch.StartNew(); while (!done && sw.ElapsedMilliseconds < timeout)) and raise a timeout alarm when the loop ends without the state.",
            "경과 시간을 잰다(var sw = Stopwatch.StartNew(); while (!done && sw.ElapsedMilliseconds < timeout)). 상태가 되지 않고 루프가 끝나면 타임아웃 알람을 낸다.",
            "Repro_ESL011_StuckSensorHangsSequence");

        public static readonly ImmutableArray<RuleInfo> All = ImmutableArray.Create(
            TickCount32, WallClockInterval, TimerReentrancy, ReadWithoutTimeout, UntimedWait, NonAtomicWrite,
            UnlockedSharedBuffer, UnboundedQueue, InvokeUnderLock, StartBeforeReady, PollWithoutDeadline);

        public static RuleInfo? Find(string id) => All.FirstOrDefault(r => r.Id == id);
    }
}
