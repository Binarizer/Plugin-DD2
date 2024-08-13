using BepInEx;
using BepInEx.Unity.Mono;
using HarmonyLib;
using Mortal;
using System;
using System.Collections.Generic;

[BepInPlugin("binarizer.plugin.mortal", "plugin by Binarizer", "1.0.0")]
public class PluginBinarizer : BaseUnityPlugin
{
    void RegisterHook(IHook hook)
    {
        hook.OnRegister(this);
        hook.GetRegisterTypes().Do(t => { Harmony.CreateAndPatchAll(t); Console.WriteLine("Patch " + t.Name); });
        hooks.Add(hook);
    }

    private readonly List<IHook> hooks = new List<IHook>();

    void Awake()
    {
        Console.WriteLine("Main Awake: Initialize Hooks");
        RegisterHook(new HookMods());
        RegisterHook(new HookExporter());
        RegisterHook(new HookDataTable());
        RegisterHook(new HookCustomStory());
    }

    public delegate void HandlerNoParam();
    public HandlerNoParam onGui;
    public HandlerNoParam onStart;
    public HandlerNoParam onUpdate;

    void Start()
    {
        onStart?.Invoke();
    }
    void Update()
    {
        onUpdate?.Invoke();
    }
    private void OnGUI()
    {
        onGui?.Invoke();
    }
}
