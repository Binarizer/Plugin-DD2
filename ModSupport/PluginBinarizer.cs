using System;
using System.Collections.Generic;
using HarmonyLib;
using BepInEx;
using BepInEx.Unity.Mono;

namespace Mortal
{
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
        }   

        void Start()
        {
            Console.WriteLine("Main Start: ");
        }

        void Update()
        {
            //Console.WriteLine("Main Update: ");
            foreach (IHook hook in hooks)
            {
                hook.OnUpdate();
            }
        }
    }
}
