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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ArgusLabs.WorldEngineClient.Communications.Prover
{
    public abstract class CloudProverBase
    {
        private readonly string _proverUri;
        
        public CloudProverBase(string proverUri)
        {
            _proverUri = proverUri;
        }

        protected async Task<(ProofResponse, bool)> RequestProofAsync(GenerateProofRequestMsg msg, CancellationToken cancellation = default)
        {
            string proofRequestJson = JsonUtility.ToJson(msg)
                .Replace("\\\"", "\"")
                .Replace("\"{", "{")
                .Replace("}\"", "}");
            
            Debug.Log($"RequestProofAsync: Requesting from: {_proverUri}");
            
            const int maxAttempts = 3;
            const int timeout = 15;
            
            var post = UnityWebRequest.Post(_proverUri, postData: proofRequestJson, "application/json");
            post.timeout = timeout;
            
            var request = post.SendWebRequest();
            
            void LogRequestInfo()
            {
                if (!Debug.isDebugBuild)
                    return;
                
                var sb = new StringBuilder();
                sb.AppendLine("RequestProofAsync: Done");
                sb.AppendLine($"_proverUri: {_proverUri}");
                sb.AppendLine($"proofRequestJson: {proofRequestJson}");
                sb.AppendLine($"POST Result: {post?.result.ToString()}");
                sb.AppendLine($"Response Code: {post?.responseCode}");
                sb.AppendLine($"post.Error: {post?.error}");

                if (post.isDone)
                {
                    sb.AppendLine($"downloadHandlerBuffer.Text: {post.downloadHandler?.text}");
                    sb.AppendLine($"downloadHandlerBuffer.Data.Length: {post.downloadHandler?.data?.Length}");
                }
                
                Debug.Log(sb.ToString());
            }

            for (int i = 0; (i < maxAttempts) && !request.isDone; ++i)
            {
                while (!(request.isDone || cancellation.IsCancellationRequested))
                    await Task.Yield();

                if (!request.isDone)
                    Debug.LogError($"Proof response timed out after {timeout} seconds.{((i < maxAttempts - 1) ? "Retrying." : "")}");
            }

            if (!request.isDone)
            {
                Debug.LogError($"Proof response timed out after {maxAttempts} attempts.");
                LogRequestInfo();
                return (default, false);
            }

            if (post.downloadHandler is null)
            {
                Debug.LogError($"post.downloadHandler is null.");
                LogRequestInfo();
                return (default, false);
            }
            
            if (post.downloadHandler.data is null)
            {
                Debug.LogError($"post.downloadHandler.data is null.");
                LogRequestInfo();
                return (default, false);
            }

            ProofResponse response = default;
            bool succeeded = false;

            try
            {
                response = JsonUtility.FromJson<ProofResponse>(post.downloadHandler.text);
                succeeded = post.result == UnityWebRequest.Result.Success;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"\n{post.downloadHandler?.text}");
                Debug.LogException(e);
            }
            finally
            {
                LogRequestInfo();
            }
            
            post.Dispose();

            return (response, succeeded);
        }
    }
}