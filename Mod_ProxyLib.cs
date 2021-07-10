using RenpyParser;
using RenpyParser.ProxyLib;
using RenPyParser.Transforms;
using RenPyParser.VGPrompter.DataHolders;
using SimpleExpressionEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TestDDLCMod
{
    public class Mod_ProxyLib : IRenpyProxyLib
    {
        private IContextAccess context;
        private IContextControl contextControl;

        public void gui_init(int a, int b) { }
        public bool hasattr(string ctx, string attr)
        {
            var fullname = attr;
            if (ctx != "store")
            {
                fullname = ctx.Substring("store.".Length) + "." + attr;
            }
            return context.TryGetVariableObject(fullname, out object _);
        }

        public class Character
        {
            public string name;
            public CharacterData value;
            public Character(object name = null, CharacterData kind = new CharacterData(), string image = "",
            string voice_tag = "", string what_prefix = "", string what_suffix = "", string who_prefix = "", string who_suffix = "", bool dynamic = false,
            string condition = "", bool interact = true, bool advance = true, string mode = "", string screen = "", string ctc = "", string ctc_pause = "",
            object ctc_timedpause = null, string ctc_position = "")
            {
                if (name == null)
                {
                    name = kind.name;
                }
                if (ctc == "")
                {
                    ctc = kind.ctc ?? "";
                }
                if (ctc_position == "")
                {
                    ctc_position = kind.ctc_position ?? "";
                }
                if (what_prefix == "")
                {
                    what_prefix = kind.what_prefix ?? "";
                }
                if (what_suffix == "")
                {
                    what_suffix = kind.what_suffix ?? "";
                }
                if (image == "")
                {
                    image = kind.image ?? "";
                }

                value = new CharacterData(name as string ?? "", dynamic, ctc, ctc_position, what_prefix, what_suffix, image);
            }
        }

        public class DynamicCharacter : Character
        {
            public DynamicCharacter(string name = "", CharacterData kind = new CharacterData(), string image = "",
                string voice_tag = "", string what_prefix = "", string what_suffix = "", string who_prefix = "", string who_suffix = "",
                string condition = "", bool interact = true, bool advance = true, string mode = "", string screen = "", string ctc = "", string ctc_pause = "",
                string ctc_timedpause = "", string ctc_position = "") : base(name, kind, image, voice_tag, what_prefix, what_suffix, who_prefix, who_suffix, /*dynamic=*/true,
                    condition, interact, advance, mode, screen, ctc, ctc_pause, ctc_timedpause, ctc_position)
            { }
        }

        public class Text : RenpyStandardProxyLib.Text
        {
            public Text(string style = "default", string blgrad = "",
                string brgrad = "", string caret = "", float cps = 0, float size = 0, string tlgrad = "",
                string trgrad = "", float xalign = 0, float yalign = 0, float xpos = 0, float ypos = 0) :
                base(CollectParameters(style, blgrad, brgrad, caret, cps, size, tlgrad, trgrad, xalign, yalign, xpos, ypos))
            { }

            private static RenpyCallParameter[] CollectParameters(string style, string blgrad, string brgrad, string caret, float cps, float size, string tlgrad, string trgrad, float xalign, float yalign, float xpos, float ypos)
            {
                var parameters = new List<RenpyCallParameter>();
                if (style != "default") parameters.Add(new RenpyCallParameter("style", style));
                if (blgrad != "") parameters.Add(new RenpyCallParameter("blgrad", blgrad));
                if (brgrad != "") parameters.Add(new RenpyCallParameter("brgrad", brgrad));
                if (caret != "") parameters.Add(new RenpyCallParameter("caret", caret));
                if (cps != 0) parameters.Add(new RenpyCallParameter("cps", cps.ToString()));
                if (size != 0) parameters.Add(new RenpyCallParameter("size", size.ToString()));
                if (style != "") parameters.Add(new RenpyCallParameter("style", style));
                if (tlgrad != "") parameters.Add(new RenpyCallParameter("tlgrad", tlgrad));
                if (trgrad != "") parameters.Add(new RenpyCallParameter("trgrad", trgrad));
                if (xalign != 0) parameters.Add(new RenpyCallParameter("xalign", xalign.ToString()));
                if (xpos != 0) parameters.Add(new RenpyCallParameter("xpos", xpos.ToString()));
                if (yalign != 0) parameters.Add(new RenpyCallParameter("yalign", yalign.ToString()));
                if (ypos != 0) parameters.Add(new RenpyCallParameter("ypos", ypos.ToString()));
                return parameters.ToArray();
            }
        }

        public class ParameterizedText : Text { }

        public class LiveTile : RenpyStandardProxyLib.Image
        {
            public LiveTile(string filename, float xalign = 0.5f, float yalign = 1) : base(filename, xalign, yalign)
            { }
        }

        public class SnowBlossom : IDisplayable
        {
            public SnowBlossom(object d, float count = 100, object xspeed = null, object yspeed = null, float start = 0, bool fast = false, bool horizontal = false)
            {
                // stub for now
            }

            public bool Complete { get; private set; }

            public void Apply(IContext context)
            {
            }

            public bool Immediate(GameObject gameObject, CurrentTransform currentTransform)
            {
                return true;
            }

            public IEnumerator Update(GameObject gameObject, CurrentTransform currentTransform)
            {
                yield return null;
            }
        }

        public class Fixed : IDisplayable
        {
            public Fixed(params object[] elements)
            {
                // stub for now
            }

            public bool Complete { get; private set; }

            public void Apply(IContext context)
            {
            }

            public bool Immediate(GameObject gameObject, CurrentTransform currentTransform)
            {
                return true;
            }

            public IEnumerator Update(GameObject gameObject, CurrentTransform currentTransform)
            {
                yield return null;
            }
        }

        public class ConditionSwitch : IDisplayable
        {
            bool predict_all { get; }
            readonly List<Condition> conditions = new List<Condition>();

            public ConditionSwitch(object predict_all, params object[] args)
            {
                this.predict_all = predict_all != null && (bool)predict_all;
                for (var i = 0; i < args.Length; i += 2)
                {
                    var rawDisplayable = args[i + 1];
                    IDisplayable displayable;
                    if (rawDisplayable is string str)
                    {
                        displayable = new RenpyLoadImage(str, str);
                    }
                    else
                    {
                        displayable = rawDisplayable as IDisplayable;
                    }
                    conditions.Add(new Condition()
                    {
                        condition = args[i] as string,
                        displayable = displayable,
                    });
                }
                Debug.Log("Building a ConditionSwitch");
                Debug.Log(Environment.StackTrace);
            }
            public ConditionSwitch(params object[] args) : this(null, args)
            { }

            public bool Complete { get; private set; }


            public void Apply(IContext context)
            {
            }
            public bool Immediate(GameObject gameObject, CurrentTransform currentTransform)
            {
                Complete = false;
                bool displayed = false;
                foreach (var condition in conditions)
                {
                    if (ExpressionRuntime.Execute(SimpleExpressionEngine.Parser.Compile(condition.condition)).GetBool())
                    {
                        if (!displayed)
                        {
                            condition.displayable.Immediate(gameObject, currentTransform);
                            displayed = true;
                            if (!predict_all)
                            {
                                break;
                            }
                        }
                    }
                }
                Complete = true;
                return false;
            }

            public IEnumerator Update(GameObject gameObject, CurrentTransform currentTransform)
            {
                yield return null;
            }
            public struct Condition
            {
                public string condition;
                public IDisplayable displayable;
            }
        }

        public void SetupProxyLib(IContextControl contextControl, ILoadSave loadSave)
        {
            context = Renpy.CurrentContext;
            this.contextControl = contextControl;

            context.AddScope("gui");
            context.SetVariableObject("gui.init", new FunctionRedirect(this, "gui_init"));
            context.SetVariableObject("pass", null); // easier than actually making the python interpreter tokenize this
            context.SetVariableObject("nvl", new CharacterData()); // might special case this later, but stub for now
        }
    }
}
