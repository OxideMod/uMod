﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

using Oxide.Core.Plugins;

namespace Oxide.Core.Libraries
{
    /// <summary>
    /// The timer library
    /// </summary>
    public class Timer : Library
    {
        private static Stopwatch stopwatch = Stopwatch.StartNew();

        private static float CurrentTime => (float)stopwatch.Elapsed.TotalSeconds;

        /// <summary>
        /// Represents a single timer instance
        /// </summary>
        public class TimerInstance
        {
            /// <summary>
            /// Gets the number of repetitions left on this timer
            /// </summary>
            public int Repetitions { get; private set; }

            /// <summary>
            /// Gets the delay between each repetition
            /// </summary>
            public float Delay { get; private set; }

            /// <summary>
            /// Gets the callback delegate
            /// </summary>
            public Action Callback { get; private set; }

            /// <summary>
            /// Gets if this timer has been destroyed
            /// </summary>
            public bool Destroyed { get; private set; }

            /// <summary>
            /// Gets the plugin to which this timer belongs, if any
            /// </summary>
            public Plugin Owner { get; private set; }

            // The next rep time
            internal float nextrep;

            /// <summary>
            /// Initialises a new instance of the TimerInstance class
            /// </summary>
            /// <param name="repetitions"></param>
            /// <param name="delay"></param>
            /// <param name="callback"></param>
            public TimerInstance(int repetitions, float delay, Action callback, Plugin owner)
            {
                Repetitions = repetitions;
                Delay = delay;
                Callback = callback;
                nextrep = CurrentTime + delay;
                Owner = owner;
                if (owner != null) owner.OnRemovedFromManager += owner_OnRemovedFromManager;
            }

            /// <summary>
            /// Called when the owner plugin was unloaded
            /// </summary>
            /// <param name="sender"></param>
            /// <param name="manager"></param>
            private void owner_OnRemovedFromManager(Plugin sender, PluginManager manager)
            {
                Destroy();
            }

            /// <summary>
            /// Destroys this timer
            /// </summary>
            public void Destroy()
            {
                Destroyed = true;
                if (Owner != null) Owner.OnRemovedFromManager -= owner_OnRemovedFromManager;
            }

            /// <summary>
            /// Updates this timer
            /// </summary>
            public void Update()
            {
                if (Destroyed) return;

                nextrep += Delay;

                try
                {
                    Callback();
                }
                catch (Exception ex)
                {
                    Destroy();
                    var error_message = string.Format("Failed to run a {0:0.00} timer", Delay);
                    if (Owner) error_message += " in " + Owner.Name;
                    Interface.GetMod().RootLogger.WriteException(error_message, ex);
                }

                if (Repetitions > 0)
                {
                    Repetitions--;
                    if (Repetitions == 0) Destroy();
                }
            }
        }
        
        public override bool IsGlobal => false;
        
        private const float updateInterval = .025f;
        private float lastUpdateAt;

        private readonly List<TimerInstance> timers = new List<TimerInstance>();

        /// <summary>
        /// Updates all timers - called every server frame
        /// </summary>
        public void Update(float delta)
        {
            var now = CurrentTime;

            if (now < lastUpdateAt)
            {
                var difference = lastUpdateAt - now - delta;
                var msg = string.Format("Time travelling detected! Timers were updated {0:0.00} seconds in the future? We will attempt to recover but this should really never happen!", difference);
                Interface.GetMod().RootLogger.Write(Logging.LogType.Warning, msg);
                foreach (var timer in timers) timer.nextrep -= difference;
                lastUpdateAt = now;
            }
            
            if (lastUpdateAt > 0f && now > lastUpdateAt + 60f)
            {
                var difference = now - lastUpdateAt - delta;
                Interface.GetMod().RootLogger.Write(Logging.LogType.Warning, "Clock is {0:0.00} seconds late! Timers have been delayed. Maybe the server froze for a long time.", difference);
                foreach (var timer in timers) timer.nextrep += difference;
            }
            
            if (now < lastUpdateAt + updateInterval) return;

            lastUpdateAt = now;

            if (timers.Count < 1) return;

            var expired = timers.TakeWhile(t => t.nextrep <= now).ToArray();
            if (expired.Length > 0)
            {
                timers.RemoveRange(0, expired.Length);
                foreach (var timer in expired)
                {
                    timer.Update();
                    // Add the timer back to the queue if it needs to fire again
                    if (!timer.Destroyed) InsertTimer(timer);
                }                    
            }
        }

        private TimerInstance AddTimer(int repetitions, float delay, Action callback, Plugin owner = null)
        {
            var timer = new TimerInstance(repetitions, delay, callback, owner);
            InsertTimer(timer);
            return timer;
        }

        private void InsertTimer(TimerInstance timer)
        {
            var index = timers.Count;
            for (var i = 0; i < timers.Count; i++)
            {
                if (timers[i].nextrep <= timer.nextrep) continue;
                index = i;
                break;
            }
            timers.Insert(index, timer);
        }

        /// <summary>
        /// Creates a timer that fires once
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <returns></returns>
        [LibraryFunction("Once")]
        public TimerInstance Once(float delay, Action callback, Plugin owner = null)
        {
            return AddTimer(1, delay, callback, owner);
        }

        /// <summary>
        /// Creates a timer that fires many times
        /// </summary>
        /// <param name="delay"></param>
        /// <param name="reps"></param>
        /// <param name="callback"></param>
        /// <param name="owner"></param>
        /// <returns></returns>
        [LibraryFunction("Repeat")]
        public TimerInstance Repeat(float delay, int reps, Action callback, Plugin owner = null)
        {
            return AddTimer(reps, delay, callback, owner);
        }

        /// <summary>
        /// Creates a timer that fires once next frame
        /// </summary>
        /// <param name="callback"></param>
        /// <returns></returns>
        [LibraryFunction("NextFrame")]
        public TimerInstance NextFrame(Action callback)
        {
            return AddTimer(1, 0.0f, callback, null);
        }
    }
}
