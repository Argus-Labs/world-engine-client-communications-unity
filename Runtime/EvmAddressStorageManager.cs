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

using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

namespace ArgusLabs.WorldEngineClient.Communications
{
    public class EvmAddressStorageManager
    {
        public string EvmAddressDataPath { get; private set; } = Path.Combine(Application.persistentDataPath, "evm-addresses.data");
        public HashSet<string> EvmAddresses { get; private set; } = new();
        
        /// <summary> Warning: Please handle file and serialization exceptions as needed. </summary>
        public bool TryStoreEvmAddress(string evmAddress)
        {
            if (!EvmAddresses.Add(evmAddress))
                return false;
            
            using FileStream file = File.OpenWrite(EvmAddressDataPath);
            var bf = new BinaryFormatter();
            bf.Serialize(file, EvmAddresses);

            return true;
        }
        
        /// <summary> Warning: Please handle file and serialization exceptions as needed. </summary>
        public bool TryLoadEvmAddresses()
        {
            if (!File.Exists(EvmAddressDataPath))
                return false;
            
            using FileStream file = File.OpenRead(EvmAddressDataPath);
            var bf = new BinaryFormatter();
            EvmAddresses = (HashSet<string>)bf.Deserialize(file);

            return true;
        }
    }
}
