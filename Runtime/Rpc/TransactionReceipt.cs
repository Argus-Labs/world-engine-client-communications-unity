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
using System.Diagnostics.CodeAnalysis;

namespace ArgusLabs.WorldEngineClient.Communications.Rpc
{
    [Serializable]
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public struct TransactionReceipt
    {
        //{"tx_hash":"0xf0ed48ab25d26ea0d1d389740415b5ff9bda3c3c86adc0235fe4a6f1193c15c5","result":{"Success":true},"errors":[]}
        public ClaimPersonaReceiptResult result;
        public string txHash;
        public string[] errors;
        
        [Serializable]
        public struct ClaimPersonaReceiptResult
        {
            public bool Success;
        }
    }
}