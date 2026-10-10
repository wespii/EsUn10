using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

static JsonElement View(Room room, string viewer = "a") => JsonSerializer.SerializeToElement(room.ToView(viewer));
static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
static string State(Room r) => View(r).GetProperty("state").GetString()!;
static string Turn(Room r) => View(r).GetProperty("turnId").GetString()!;
static void Move(FakeClock clock, Room room, double seconds, params string[] ids) { clock.Advance(seconds); foreach(var id in ids) room.Touch(id); room.Tick(); }
static (Room Room, FakeClock Clock) Game(int count=2)
{
    var clock=new FakeClock();var room=new Room("123456",new("a","Ana",null),clock);
    foreach(var id in new[]{"b","c","d"}.Take(count-1)) room.TryAdd(new(id,id,null),4);
    foreach(var id in new[]{"a","b","c","d"}.Take(count)) room.SetReady(id);
    Check(State(room)=="lobby","10 second delay");Move(clock,room,10,new[]{"a","b","c","d"}.Take(count).ToArray());
    Check(State(room)=="playing","automatic start");return(room,clock);
}
var tests = new Dictionary<string,Action> {
["reconnect, privacy and duplicate attempts"] = () => {
    var (r,c)=Game(); Check(r.TryAdd(new("a","Ana",null),4),"reconnect existing during play");Check(!r.TryAdd(new("x","Extra",null),4),"block new midgame");
    Check(View(r).GetProperty("secretNumber").ValueKind==JsonValueKind.Null,"guesser hidden");
    int secret=View(r,"b").GetProperty("secretNumber").GetInt32(), wrong=secret%10+1;var request=Guid.NewGuid().ToString();var turn=Turn(r);
    var accepted=0;Parallel.For(0,20,_=>{if(r.Guess("a",wrong,turn,request).Accepted)Interlocked.Increment(ref accepted);});
    Check(accepted==1,"one accepted duplicate");Check(!r.Guess("a",wrong,turn,Guid.NewGuid().ToString()).Accepted,"repeated number rejected");Check(View(r).GetProperty("guessesRemaining").GetInt32()==2,"only one attempt used");
    Check(!r.Guess("b",secret,turn,Guid.NewGuid().ToString()).Accepted,"wrong player rejected");
    Check(r.Guess("a",secret,turn,Guid.NewGuid().ToString()).Accepted,"correct guess");Check(State(r)=="reveal","reveal phase");Check(View(r).GetProperty("secretNumber").GetInt32()==secret,"reveal visible to guesser");
    Move(c,r,3,"a","b");Check(State(r)=="reveal","four full seconds");Move(c,r,1,"a","b");Check(State(r)=="playing","next turn");Check(!r.Guess("a",secret,turn,Guid.NewGuid().ToString()).Accepted,"stale command rejected");
},
["three full rounds, scoring, finish and rematch"] = () => {
    var (r,c)=Game();for(var i=0;i<6;i++){var active=View(r).GetProperty("activePlayerId").GetString()!;var other=active=="a"?"b":"a";var secret=View(r,other).GetProperty("secretNumber").GetInt32();Check(r.Guess(active,secret,Turn(r),Guid.NewGuid().ToString()).Accepted,"turn accepted");Move(c,r,4,"a","b");}
    Check(State(r)=="finished","finish after six turns");Check(View(r).GetProperty("players").EnumerateArray().All(p=>p.GetProperty("score").GetInt32()==9),"equal opportunities and scores");
    Check(!r.Rematch("b"),"only host rematch");Check(r.Rematch("a"),"rematch");Check(State(r)=="lobby","rematch lobby");Check(View(r).GetProperty("players").EnumerateArray().All(p=>!p.GetProperty("ready").GetBoolean()&&p.GetProperty("score").GetInt32()==0),"scores and ready reset");
},
["near miss scoring and timeout reveal"] = () => {
    var (r,c)=Game();int secret=View(r,"b").GetProperty("secretNumber").GetInt32(), near=secret==10?9:secret+1;foreach(var n in Enumerable.Range(1,10).Where(n=>n!=secret&&n!=near).Take(2).Append(near))r.Guess("a",n,Turn(r),Guid.NewGuid().ToString());
    Check(View(r).GetProperty("players")[0].GetProperty("score").GetInt32()==2,"two points near final");Move(c,r,4,"a","b");Move(c,r,90,"a","b");Check(State(r)=="reveal","timeout reveals card");Check(View(r,"b").GetProperty("secretNumber").ValueKind==JsonValueKind.Number,"timeout number visible");
},
["hint cooldown, cap, card matching, feedback, manual limits"] = () => {
    var (r,c)=Game(3);int secret=View(r,"b").GetProperty("secretNumber").GetInt32();var hints=new HashSet<string>();
    Check(!r.GetHint("a",Turn(r)).Accepted,"guesser cannot request hint");
    for(var i=0;i<3;i++){var hint=r.GetHint("b",Turn(r));Check(hint.Accepted,"hint accepted");Check(HintCatalog.Cards.Any(h=>h.Number==secret&&h.Text==hint.Message),"exact card value");Check(hints.Add(hint.Message),"no repeat");Check(!r.GetHint("c",Turn(r)).Accepted,"shared cooldown");Move(c,r,8,"a","b","c");}
    Check(!r.GetHint("b",Turn(r)).Accepted,"three hints maximum");Check(r.AddClue("b","es puntual",Turn(r)).Accepted,"manual clue");Check(!r.AddClue("b","otra",Turn(r)).Accepted,"manual spam blocked");
    var turn=Turn(r);r.Guess("a",secret,turn,Guid.NewGuid().ToString());Check(r.RateHints("b",turn)==null,"only guesser rates");Check(r.RateHints("a",turn)?.Length==3,"ratings apply to generated hints");Check(r.RateHints("a",turn)==null,"no duplicate vote");
},
["disconnect grace, host transfer, leaving and expiry"] = () => {
    var (r,c)=Game(4);Move(c,r,26,"b","c","d");Check(!View(r,"b").GetProperty("players")[0].GetProperty("online").GetBoolean(),"disconnected indicator");Check(r.HostId=="b","host transfers");Check(State(r)=="playing","grace before skip");Move(c,r,35,"b","c","d");Check(State(r)=="reveal","skip disconnected turn");Move(c,r,4,"b","c","d");Check(View(r,"b").GetProperty("activePlayerId").GetString()=="b","next present player");Check(r.TryLeave("b"),"leave active game");Check(State(r)=="reveal","leaving active player reveals");Check(r.TryLeave("c"),"another leave");Check(State(r)=="finished","finish if fewer than two members");c.Advance(1801);Check(r.IsExpired(),"abandoned room expires");
},
["countdown cancellation and lobby cleanup"] = () => {
    var c=new FakeClock();var r=new Room("000000",new("a","Ana",null),c);r.SetReady("a");Check(View(r).GetProperty("countdownEndsAt").ValueKind==JsonValueKind.Null,"solo never starts");r.TryAdd(new("b","Ben",null),4);r.SetReady("b");r.SetReady("b");Check(View(r).GetProperty("countdownEndsAt").ValueKind==JsonValueKind.Null,"unready cancels");r.SetReady("b");Move(c,r,5,"a","b");r.TryAdd(new("c","Caro",null),4);Check(View(r).GetProperty("countdownEndsAt").ValueKind==JsonValueKind.Null,"join cancels");Move(c,r,121,"b");Check(View(r,"b").GetProperty("players").GetArrayLength()==1,"stale lobby players removed");
},
["shuffled decks across rematches and final-attempt distance scoring"] = () => {
    var (r,c)=Game(4);var dealt=new List<int>();var ids=new[]{"a","b","c","d"};var directionChecks=new HashSet<int>();
    for(var match=0;match<5;match++)
    {
        for(var i=0;i<12;i++)
        {
            var active=View(r).GetProperty("activePlayerId").GetString()!;
            var other=ids.First(id=>id!=active);var secret=View(r,other).GetProperty("secretNumber").GetInt32();dealt.Add(secret);
            var before=View(r).GetProperty("players").EnumerateArray().First(p=>p.GetProperty("id").GetString()==active).GetProperty("score").GetInt32();
            var distance=i%4;var final=distance>0&&!directionChecks.Contains(-distance)&&secret-distance>=1?secret-distance:secret+distance<=10?secret+distance:secret-distance;directionChecks.Add(final-secret);
            var firstTwo=Enumerable.Range(1,10).Where(n=>n!=secret&&n!=final).Take(2).ToArray();
            foreach(var n in firstTwo)
            {
                Check(r.Guess(active,n,Turn(r),Guid.NewGuid().ToString()).Accepted,"intermediate guess");
                Check(View(r).GetProperty("players").EnumerateArray().First(p=>p.GetProperty("id").GetString()==active).GetProperty("score").GetInt32()==before,"no proximity points before third attempt");
            }
            Check(r.Guess(active,final,Turn(r),Guid.NewGuid().ToString()).Accepted,"final guess");
            var after=View(r).GetProperty("players").EnumerateArray().First(p=>p.GetProperty("id").GetString()==active).GetProperty("score").GetInt32();
            Check(after-before==(distance==0?3:distance==1?2:distance==2?1:0),"correct distance points");Move(c,r,4,ids);
        }
        Check(State(r)=="finished","full match finished");
        if(match<4){Check(r.Rematch(r.HostId),"rematch preserves deck");foreach(var id in ids)r.SetReady(id);Move(c,r,10,ids);}
    }
    Check(new[]{-2,-1,1,2}.All(directionChecks.Contains),"checked proximity both above and below");
    for(var i=0;i<dealt.Count;i+=10)Check(dealt.Skip(i).Take(10).Distinct().Count()==10,"every deck contains each value exactly once");
    for(var i=1;i<dealt.Count;i++)Check(dealt[i]!=dealt[i-1],"no immediate repetition including deck boundaries");
},
["host settings, ready reset, deadlines, goal victory and rematch"] = () => {
    var c=new FakeClock();var r=new Room("555555",new("a","Ana",null),c);r.TryAdd(new("b","Ben",null),4);
    Check(!r.Configure("b",30,5).Accepted,"guest cannot configure");Check(!r.Configure("x",30,5).Accepted,"outsider cannot configure");
    foreach(var invalid in new[]{(29,5),(301,5),(90,-1),(90,101)})Check(!r.Configure("a",invalid.Item1,invalid.Item2).Accepted,"invalid bounds rejected");
    r.SetReady("a");r.SetReady("b");Check(r.Configure("a",30,3).Accepted,"host configures countdown");Check(View(r).GetProperty("countdownEndsAt").ValueKind==JsonValueKind.Null,"settings cancel countdown");Check(View(r).GetProperty("players").EnumerateArray().All(p=>!p.GetProperty("ready").GetBoolean()),"settings clear readiness");
    r.SetReady("a");r.SetReady("b");Move(c,r,10,"a","b");var view=View(r);
    Check((view.GetProperty("deadline").GetDateTimeOffset()-view.GetProperty("turnStartedAt").GetDateTimeOffset()).TotalSeconds==30,"configured deadline");Check(!r.Configure("a",300,100).Accepted,"settings frozen during game");
    var secret=View(r,"b").GetProperty("secretNumber").GetInt32();r.Guess("a",secret,Turn(r),Guid.NewGuid().ToString());Check(State(r)=="reveal","winner still gets reveal");Move(c,r,4,"a","b");Check(State(r)=="finished","target finishes early");Check(r.Rematch("a"),"rematch allowed");Check(View(r).GetProperty("settings").GetProperty("turnSeconds").GetInt32()==30,"rematch retains settings");
    Check(r.Configure("a",300,100).Accepted,"new settings before rematch");r.SetReady("a");r.SetReady("b");Move(c,r,10,"a","b");
    Move(c,r,90,"a","b");Check(State(r)=="playing","longer time respected");Move(c,r,210,"a","b");Check(State(r)=="reveal","longer time expires");Move(c,r,4,"a","b");
    for(var i=1;i<6;i++){Move(c,r,300,"a","b");Move(c,r,4,"a","b");}Check(State(r)=="playing"&&View(r).GetProperty("round").GetInt32()==4,"target mode continues beyond three rounds");
},
["remembered login survives a new server and rejects tampering"] = () => {
    var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Discord:ClientSecret","test-only-key-not-a-real-discord-secret"}}).Build();
    var remembered=new RememberLogin(config,new TestEnvironment());var original=new DefaultHttpContext();original.Session=new TestSession();remembered.Issue(original,new("u123","Test",null));
    var cookie=original.Response.Headers.SetCookie.ToString();Check(cookie.Contains("httponly")&&cookie.Contains("secure")&&cookie.Contains("max-age"),"persistent secure cookie");cookie=cookie.Split(';')[0];
    var restored=new DefaultHttpContext();restored.Session=new TestSession();restored.Request.Headers.Cookie=cookie;new RememberLogin(config,new TestEnvironment()).Restore(restored);Check(restored.Session.GetString("discord_id")=="u123","restore across instance");
    var changed=new DefaultHttpContext();changed.Session=new TestSession();changed.Request.Headers.Cookie=cookie+"x";remembered.Restore(changed);Check(changed.Session.GetString("discord_id")==null,"tampered cookie rejected");remembered.Forget(restored);Check(restored.Response.Headers.SetCookie.ToString().Contains("1970"),"logout removes remember cookie");
}
};
foreach(var test in tests){test.Value();Console.WriteLine("PASS "+test.Key);}Console.WriteLine($"{tests.Count} suites passed.");

sealed class FakeClock : TimeProvider { private DateTimeOffset now=new(2026,10,10,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance(double seconds)=>now=now.AddSeconds(seconds); }
sealed class TestEnvironment : IWebHostEnvironment { public string ApplicationName{get;set;}="Tests";public string EnvironmentName{get;set;}="Production";public string WebRootPath{get;set;}="";public string ContentRootPath{get;set;}="";public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider(); }
sealed class TestSession : ISession { private readonly Dictionary<string,byte[]> data=new();public bool IsAvailable=>true;public string Id=>"test";public IEnumerable<string> Keys=>data.Keys;public void Clear()=>data.Clear();public Task CommitAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;public Task LoadAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;public void Remove(string key)=>data.Remove(key);public void Set(string key,byte[] value)=>data[key]=value;public bool TryGetValue(string key,[System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out byte[]? value)=>data.TryGetValue(key,out value); }
