using System;
using System.Collections.Generic;
using System.Reflection;
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple=true)]
    public class HarmonyPatch : Attribute { public HarmonyPatch(Type type, string name) {} }
    public class HarmonyPrefix : Attribute {}
    public class HarmonyFinalizer : Attribute {}
    public class Traverse
    {
        object target; MethodInfo method;
        public static Traverse Create(object target) { return new Traverse { target=target }; }
        public Traverse Method(string name, Type[] types) { method=target.GetType().GetMethod(name,types); return this; }
        public object GetValue(params object[] args) { return method.Invoke(target,args); }
        public T GetValue<T>(params object[] args) { return (T)GetValue(args); }
    }
}
namespace Mortal.Core
{
    public class GameSave { public int Money; public List<string> StoryLogs = new List<string>(); }
    public class SaveSystem
    {
        public static SaveSystem Instance = new SaveSystem();
        public int Money = 77;
        public GameSave CreateSaveData(bool includeStoryLog = true)
        { if (includeStoryLog) throw new Exception("Snapshot requested native restart history"); return new GameSave { Money=Money }; }
        public void ExecuteLoadGameData(GameSave save) { Money=save.Money; }
    }
    public static class StoryLogData
    {
        public static List<string> Entries = new List<string>();
        public static void Load(GameSave save)
        {
            LOMSave.Probe.NativeStoryLog.BeginLoad();
            try { Entries.Clear(); foreach (var line in save.StoryLogs) AddEntry(line); }
            finally { LOMSave.Probe.NativeStoryLog.EndLoad(null); }
        }
        public static void AddEntry(string line) { if (LOMSave.Probe.NativeStoryLog.BeforeAdd()) Entries.Add(line); }
        public static void Save(GameSave save, bool restart)
        { if (restart) throw new Exception("Current history was truncated"); save.StoryLogs = new List<string>(Entries); }
    }
}
namespace LOMSave.Probe
{
    internal static class StoryContinuation { internal static bool IsPending, NativeSaving; }
    internal static class ReplayStateValidator { internal static bool IsRestoring; }
    internal static class StoryCommandTrace { internal static bool IsReplayPending; internal static object Current; }
    public static class NativeStoryLogTests
    {
        static int checks;
        static void Check(bool ok,string why) { checks++; if (!ok) throw new Exception(why); }
        public static string Run()
        {
            var system=Mortal.Core.SaveSystem.Instance;
            Mortal.Core.StoryLogData.AddEntry("saved dialogue");
            var save=NativeSaveApi.Capture(system);
            Check(save.Money==77 && save.StoryLogs.Count==1,"New bool snapshot signature returns complete current history");
            StoryContinuation.IsPending=true;
            Mortal.Core.StoryLogData.AddEntry("reconstructed dialogue");
            Check(Mortal.Core.StoryLogData.Entries.Count==1,"Restoration cannot duplicate visible dialogue");
            system.Money=3;
            NativeSaveApi.Load(save);
            Check(system.Money==77 && Mortal.Core.StoryLogData.Entries.Count==1,"Restore loads history even while direct continuation pending");
            Check(!NativeStoryLog.BeforeAdd(),"Load guard releases after success");
            NativeStoryLog.BeginLoad(); NativeStoryLog.BeginLoad();
            var error=new Exception("load failure");
            Check(ReferenceEquals(NativeStoryLog.EndLoad(error),error) && NativeStoryLog.BeforeAdd(),"Nested exception preserves outer load and original exception");
            NativeStoryLog.EndLoad(error);
            Check(!NativeStoryLog.BeforeAdd(),"Exception releases log initialization scope");
            StoryContinuation.IsPending=false;
            Mortal.Core.StoryLogData.AddEntry("next dialogue");
            Check(Mortal.Core.StoryLogData.Entries.Count==2,"Continuing normally records next dialogue once");
            ReplayStateValidator.IsRestoring=true; Check(!NativeStoryLog.BeforeAdd(),"Migration suppresses incidental log writes"); ReplayStateValidator.IsRestoring=false;
            StoryCommandTrace.IsReplayPending=true; Check(!NativeStoryLog.BeforeAdd(),"Legacy replay cannot append duplicate history"); StoryCommandTrace.IsReplayPending=false;
            StoryCommandTrace.Current=new object();
            bool restart=true; NativeStoryLog.BeforeSave(ref restart); Check(!restart,"Free checkpoint saves all current log entries");
            StoryContinuation.NativeSaving=true; restart=true; NativeStoryLog.BeforeSave(ref restart); Check(restart,"Native story saves retain native restart-prefix behavior");
            StoryContinuation.NativeSaving=false; StoryCommandTrace.Current=null;
            restart=true; NativeStoryLog.BeforeSave(ref restart); Check(restart,"Other native saves unchanged");
            return checks+" native snapshot/history compatibility assertions passed";
        }
    }
}
