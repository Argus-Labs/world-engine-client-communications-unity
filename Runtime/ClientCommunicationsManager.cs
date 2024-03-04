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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArgusLabs.WorldEngineClient.Communications.Rpc;
using Nakama;
using Nakama.TinyJson;
using UnityEngine;

namespace ArgusLabs.WorldEngineClient.Communications
{
    public abstract class ClientCommunicationsManager
    {
        protected const int TimeoutDuration = 10;

        private readonly Client _client;
        private ISession _session;
        private string _deviceId;
        
        public ISocket Socket { get; private set; }

        /// <summary>
        /// CommunicationsManager will fall back to this when no CancellationToken is provided in the various methods where it could be required.
        /// As of 11/15/2023, currently everything just passes in the CancellationToken from the top level, so this really is a fallback.
        /// For a more robust implementation, we could link the tokens, etc. but we're very unlikely to need anything like that for this project.
        /// </summary>
        public CancellationToken DefaultCancellationToken { get; set; }

        public ClientCommunicationsManager(Client client)
        {
            _client = client;
        }

        public async ValueTask<bool> AutoSignInAsync(string uid, CancellationToken cancellation = default)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            _deviceId = uid;

            try
            {
                var retryConfig = new RetryConfiguration(2000, 5);

                try
                {
                    _session = await _client.AuthenticateDeviceAsync(_deviceId, retryConfiguration: retryConfig, canceller: cancellation);
                }
                catch (ApiResponseException)
                {
                    Debug.LogError($"Could not authenticate device: '{_deviceId}'");
                    throw;
                }

                Debug.Log($"Session: Created? {_session.Created}  IsExpired? {_session.IsExpired} Auth Token: {_session.AuthToken}");
                Debug.Log($"_session.UserId: {_session.UserId}");
                Debug.Log($"_session.Username: {_session.Username}");
                Debug.Log("Authenticated.");

                // No cleanup. No worries. Lets see.
                Socket = _client.NewSocket(useMainThread: true);
                await Socket.ConnectAsync(_session, appearOnline: true, connectTimeout: TimeoutDuration);

                if (Socket.IsConnected)
                {
                    Debug.Log($"Socket.IsConnected? {Socket.IsConnected}");
                }
                else
                {
                    Debug.LogError("Could not open socket.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }

            return true;
        }

        // The only real value this method provides is verbose debug logging.
        public async ValueTask<SocketResponse<TransactionReceipt>> TryClaimPersonaAsync(string personaTag, CancellationToken cancellation = default)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            SocketResponse<TransactionReceipt> response = await ClaimPersonaAsync(new PersonaMsg { personaTag = personaTag }, cancellation);

            if (!response.Result.WasSuccessful)
            {
                Debug.LogError($"Could not claim persona. Payload: {response.Result.Payload}");
            }
            else if (response.IsComplete)
            {
                Debug.Log($"{nameof(ClaimPersonaAsync)} was success? {response.Content.result.Success}");
                Debug.Log($"{nameof(ClaimPersonaAsync)} tx hash: {response.Content.txHash}");
                Debug.Log($"{nameof(ClaimPersonaAsync)} original rpc response payload: {response.Result.Payload}");

                if (response.Content.errors?.Length > 0)
                    Debug.Log($"{nameof(ClaimPersonaAsync)} errors: {string.Join(", ", "response.Content.errors)}")}");
            }
            else
            {
                Debug.LogWarning("Persona may have been claimed, but the tx response timed out.");
            }

            return response;
        }

        // The only real value this method provides is verbose debug logging.
        // Note: It also sets the player's PersonaTag.
        public async ValueTask<(RpcResult, ShowPersonaMsg)> TryShowPersonaAsync(string personaTag, CancellationToken cancellation = default)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            RpcResult rpcResult = await ShowPersonaAsync(new PersonaMsg { personaTag = personaTag }, cancellation);
            ShowPersonaMsg msg = default;

            Debug.Log($"Was successful? {rpcResult.WasSuccessful} {rpcResult.Payload}");

            if (rpcResult.WasSuccessful)
                msg = JsonUtility.FromJson<ShowPersonaMsg>(rpcResult.Payload);
            else
                Debug.LogError(rpcResult.Error.message);

            return (rpcResult, msg);
        }

        private async ValueTask EnsureTokensAreStillValid(CancellationToken cancellation = default)
        {
            if (_session.HasExpired(DateTime.UtcNow.AddMinutes(2)))
            {
                try
                {
                    // Get a new access token. This triggers non-stop. Don't think it should.
                    // Because the behavior didn't feel like it matched the docs,
                    // this matches the docs exactly aside from adding 2 minutes instead of a day.
                    _session = await _client.SessionRefreshAsync(_session, canceller: cancellation);
                }
                catch (ApiResponseException)
                {
                    // Get a new refresh token.
                    _session = await _client.AuthenticateDeviceAsync(_deviceId, canceller: cancellation);
                    Debug.Log("New refresh token received.");
                    Debug.Log($"Session: Created? {_session.Created} IsExpired? {_session.IsExpired} Auth Token: {_session.AuthToken}");
                    Debug.Log($"_session.UserId: {_session.UserId}");
                    Debug.Log($"_session.Username: {_session.Username}");
                }
            }
        }

        protected async ValueTask<RpcResult> RpcAsync(string rpcId, string payload, CancellationToken cancellation = default)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            RpcResult result = default;

            if (_session is null)
            {
                Debug.Log($"RpcAsync: Backend is unavailable for '{rpcId}'.");
                return result;
            }

            await EnsureTokensAreStillValid(cancellation);

            try
            {
                Debug.Log($"RpcAsync: RPC: '{rpcId}' Payload: '{payload}'");

                var response = await _client.RpcAsync(_session, rpcId, payload, canceller: cancellation);
                result.Payload = response.Payload;
                result.WasSuccessful = true;
            }
            catch (Exception ex)
            {
                result.Payload = string.Empty;
                result.WasSuccessful = false;

                // The app is quitting and this is still running async, so just bail.
                if (Application.exitCancellationToken.IsCancellationRequested)
                    return result;

                if (ex.InnerException is ApiResponseException innerEx)
                {
                    if (innerEx?.Message is not null)
                        result.Error = JsonUtility.FromJson<BackendError>(innerEx.Message);

                    result.StatusCode = innerEx.StatusCode;
                }
                else
                {
                    Debug.LogException(ex);
                }
            }

            Debug.Log($"RpcAsync: RPC: '{rpcId}' was success? {result.WasSuccessful}");

            return result;
        }

        protected async ValueTask<SocketResponse<T>> SocketRequestAsync<T>(
            string rpcId, string payload, CancellationToken cancellation = default, float timeout = TimeoutDuration)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            var response = new SocketResponse<T>();
            bool wasReceived = false;
            
            Socket.ReceivedNotification += OnReceivedNotification;

            async void OnReceivedNotification(IApiNotification notification)
            {
                if (notification.Subject != "receipt") // <-- magic!
                    return;
                
                wasReceived = true;

                await Awaitable.NextFrameAsync(cancellation);
                await Awaitable.NextFrameAsync(cancellation);
                
                Debug.Log($"OnReceivedNotification: Subject: {notification.Subject} Content: {notification.Content} Id: {notification.Id} SenderId: {notification.SenderId} Code: {notification.Code}");
                
                if (notification.Persistent)
                    Debug.LogWarning($"Received persistent notification! This is unexpected and may cause memory leaks in the backend.\nPayload: {notification.Content}");
                
                if (string.IsNullOrWhiteSpace(notification.Content))
                {
                    Debug.LogError($"Notification content is empty: '{notification.Content}'");
                    response.IsComplete = true;
                    return;
                }
                
                Debug.Log($"response.Result.Payload: {response.Result.Payload}");
                
                var receipt = JsonUtility.FromJson<TransactionReceipt>(notification.Content);

                if (string.IsNullOrWhiteSpace(response.Result.Payload))
                {
                    var responseTxHash = JsonUtility.FromJson<TxHash>(response.Result.Payload);

                    if (receipt.txHash != responseTxHash.txHash)
                    {
                        Debug.Log("txHashes DO NOT MATCH. That's fine.");
                        return;
                    }
                }

                response.Content = notification.Content.FromJson<T>();
                response.IsComplete = true; // <-- only set when tx_hashes match or certain other conditions. See above.
            }
            
            RpcResult result = await RpcAsync(rpcId, payload, cancellation);
            response.Result = result;
            
            // Allow event to wait for processing.
            await Awaitable.NextFrameAsync(cancellation);
            await Awaitable.NextFrameAsync(cancellation);
            
            // Wait for the event to be processed.
            await Awaitable.NextFrameAsync(cancellation);
            await Awaitable.NextFrameAsync(cancellation);
            
            Socket.ReceivedNotification -= OnReceivedNotification;
            
            if (!wasReceived)
            {
                response.IsComplete = false;
                return response;
            }
            
            if (result.WasSuccessful)
            {
                Debug.Log($"SocketRequestAsync was successful for: {rpcId}");
                Debug.Log($"response.Payload: {result.Payload}");

                if (string.IsNullOrWhiteSpace(result.Payload))
                {
                    Debug.Log($"result.Payload is null for: {rpcId} - This may be fine.");
                    return response;
                }

                try
                {
                    response.Content = result.Payload.FromJson<T>();
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to parse payload for: {rpcId} - {ex.Message} - This may be fine.");
                    return response;
                }
            }
            else
            {
                Debug.Log($"SocketRequestAsync was NOT successful for: {rpcId}");
                return response;
            }

            if (response.IsComplete)
            {
                Debug.Log($"Tx receipt received for: {rpcId}");
            }
            else
            {
                Debug.LogWarning($"Tx receipt timed out for: {rpcId}");
            }

            return response;
        }

        // Note: The constant RPC IDs are inlined here to avoid code duplication for no reason.
        // As soon as there's a decent reason, then we should immediately switch and provide a separate list of constants.

        private async ValueTask<SocketResponse<TransactionReceipt>> ClaimPersonaAsync(PersonaMsg msg, CancellationToken cancellation = default, float timeout = TimeoutDuration)
            => await SocketRequestAsync<TransactionReceipt>("nakama/claim-persona", msg.ToJson(), cancellation, timeout);
        
        private async ValueTask<RpcResult> ShowPersonaAsync(PersonaMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("nakama/show-persona", msg.ToJson(), cancellation);

        public async ValueTask<RpcResult> SaveAsync(PlayerDataMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("nakama/save", msg.ToJson(), cancellation);

        public async ValueTask<RpcResult> LoadPlayerDataAsync(CancellationToken cancellation = default)
            => await RpcAsync("nakama/get-save", "{}", cancellation);

        public async ValueTask<RpcResult> ClaimKeyAsync(KeyMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("claim-key", msg.ToJson(), cancellation);

        public async ValueTask<RpcResult> AuthorizePersonaAddressAsync(AuthorizePersonaAddressMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("tx/game/authorize-persona-address", msg.ToJson(), cancellation);

        public async ValueTask<RpcResult> ReadCurrentTickAsync(CancellationToken cancellation = default)
            => await RpcAsync("query/game/current-tick", "{}", cancellation);

        public async ValueTask<RpcResult> ReadPlayerRankAsync(PlayerRankMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("query/game/player-rank", msg.ToJson(), cancellation);

        public async ValueTask<RpcResult> ReadLeaderboardAsync(LeaderboardRangeMsg msg, CancellationToken cancellation = default)
            => await RpcAsync("query/game/player-range", msg.ToJson(), cancellation);
    }
}
