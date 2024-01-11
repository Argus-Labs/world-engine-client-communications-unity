// Copyright 2024 Argus Labs
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using UnityEngine;
using UnityEngine.Assertions;

namespace ArgusLabs.WorldEngineClient.Communications
{
    public class WorldClock
    {
        public ulong InitialTick { get; private set; }
        public DateTime InitialTimestamp { get; private set; }

        public ulong CurrentTick
        {
            get
            {
                ulong result = InitialTick;

                try
                {
                    result = InitialTick + SecondsToTicks((DateTime.UtcNow - InitialTimestamp).TotalSeconds);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"(CurrentTick getter) InitialTick: {InitialTick} (DateTime.UtcNow - InitialTimestamp) Diff: {(DateTime.UtcNow - InitialTimestamp)}");
                    Debug.LogError($"(CurrentTick getter) (from diff) TotalSeconds: {(DateTime.UtcNow - InitialTimestamp).TotalSeconds}");
                    Debug.LogException(ex);
                }

                return result;
            }
        }
        
        public int TicksPerSecond { get; }
        public double SecondsElapsed => TicksToSeconds(CurrentTick);
        
        public event Action Resyncing;
        
        public WorldClock(int ticksPerSecond)
        {
            Assert.IsFalse(ticksPerSecond == 0, "TicksPerSecond can't be zero.");
            TicksPerSecond = Mathf.Max(1, ticksPerSecond);
        }
        
        public void SyncToWorldEngine(ulong initialTick)
        {
            InitialTick = initialTick;
            InitialTimestamp = DateTime.UtcNow;
            Debug.Log($"SyncToWorldEngine: {initialTick}");
        }

        public DateTime TickToUtc(ulong tick)
        {
            DateTime result = DateTime.UtcNow;
            ulong currentTick = CurrentTick;
            
            try
            {
                if (currentTick < tick)
                {
                    Debug.LogWarning("(CurrentTick < tick) CurrentTick: {CurrentTick} incoming tick: {tick}\n Setting incoming tick = CurrentTick");
                    tick = currentTick;
                }
                
                result = DateTime.UtcNow - TimeSpan.FromSeconds(TicksToSeconds(currentTick - tick));
            }
            catch (Exception ex)
            {
                Debug.LogError($"(TickToUtc) CurrentTick: {currentTick} incoming tick: {tick} Diff: {currentTick - tick}");
                Debug.LogException(ex);
            }

            return result;
        }
        
        public ulong SecondsToTicks(double seconds) => (ulong)(seconds * TicksPerSecond);
        public double TicksToSeconds(double ticks) => ticks / TicksPerSecond;
        public void Resync() => Resyncing?.Invoke();
    }
}