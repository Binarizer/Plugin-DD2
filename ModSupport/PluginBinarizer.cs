using BepInEx;
using BepInEx.Unity.Mono;
using Fungus;
using HarmonyLib;
using MoonSharp.Interpreter;
using Mortal.Story;
using System;
using System.Collections.Generic;
using UnityEngine;

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
            RegisterHook(new HookCustomStory());
        }   

        void Start()
        {
            Console.WriteLine("Main Start: ");
        }

        void Update()
        {
            foreach (IHook hook in hooks)
            {
                hook.OnUpdate();
            }
            if (Input.GetKeyDown(KeyCode.F2))
            {
                consoleOn = !consoleOn;
            }
        }

        bool consoleOn = true;
        LuaEnvironment luaEnv = null;
        Rect consoleRect = new Rect(10, Screen.height - 100, 500, 80);
        string luaCmd = "";
        string luaRet = "";
        private void OnGUI()
        {
            luaEnv = Traverse.Create(LuaManager.Instance).Field("_luaEnvironment").GetValue<LuaEnvironment>();
            consoleOn = consoleOn && luaEnv != null;
            if (consoleOn)
            {
                consoleRect = GUI.Window(857205, consoleRect, new GUI.WindowFunction(DoWindow), "lua console");
            }
        }
        public void DoWindow(int windowID)
        {
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            luaCmd = GUILayout.TextField(luaCmd, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("run", GUILayout.Width(60)))
            {
                luaEnv?.DoLuaString(luaCmd, "Console.cmd", false, delegate (DynValue res)
                {
                    luaRet = res.ToString();
                });
            }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Return = {luaRet}");
            GUILayout.EndVertical();
            GUI.DragWindow();
        }
    }
}
