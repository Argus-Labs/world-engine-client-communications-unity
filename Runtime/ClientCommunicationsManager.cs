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

                var matchesResult = await _client.ListMatchesAsync(_session, min: 0, max: 100, limit: 100,
                    authoritative: true, label: string.Empty, query: string.Empty, canceller: cancellation);
                var matches = matchesResult.Matches;
                var matchInfo = matches.FirstOrDefault();

                if (matchInfo is null)
                {
                    Debug.LogError("Could not find a match.");
                    return false;
                }

                Debug.Log($"Found: MatchId: {matchInfo.MatchId} Label: {matchInfo.Label} Size: {matchInfo.Size} HandlerName: {matchInfo.HandlerName} TickRate: {matchInfo.TickRate}");

                var match = await Socket.JoinMatchAsync(matchInfo.MatchId);

                Debug.Log($"Self: {match.Self}");
                Debug.Log($"{string.Join('\n', match.Presences)}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }

            return true;
        }

        // The only real value this method provides is verbose debug logging.
        public async ValueTask<SocketResponse<ClaimPersonaReceipt>> TryClaimPersonaAsync(string personaTag, CancellationToken cancellation = default)
        {
            if (cancellation == default)
                cancellation = DefaultCancellationToken;

            SocketResponse<ClaimPersonaReceipt> response = await ClaimPersonaAsync(new PersonaMsg { personaTag = personaTag }, cancellation);

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

            void OnReceived(IMatchState matchState)
            {
                Debug.Log($"OnReceived(matchState): We received matchState for: {rpcId}");
                string json = Encoding.UTF8.GetString(matchState.State);
                Debug.Log($"OnReceived(matchState): {rpcId} json:\n{json}");

                // Even though the actual json contains much more than txHash, we'll ignore all that
                // and just pretend that txHash is all there is. Then later we'll convert the same json to typeof(T).

                if (string.IsNullOrWhiteSpace(response.Result.Payload))
                {
                    Debug.LogError($"OnReceived(matchState): Backend response payload is empty: '{response.Result.Payload}'");
                    return;
                }

                var responseTxHash = JsonUtility.FromJson<TxHash>(response.Result.Payload);
                var matchStateTxHash = JsonUtility.FromJson<TxHash>(json);

                Debug.Log($"OnReceived(matchState): responseTxHash:{responseTxHash.txHash} <- same? -> matchStateTxHash:{matchStateTxHash.txHash}");

                if (responseTxHash.txHash != matchStateTxHash.txHash)
                {
                    Debug.Log("OnReceived(matchState): txHashes DO NOT MATCH. That's fine.");
                    return;
                }

                Debug.Log("OnReceived(matchState): txHashes match.");
                response.Content = JsonUtility.FromJson<T>(json);
                response.IsComplete = true; // <-- only set when tx_hashes match!
            }

            Socket.ReceivedMatchState += OnReceived;

            var result = await RpcAsync(rpcId, payload, cancellation);
            response.Result = result;

            if (result.WasSuccessful)
            {
                Debug.Log($"SocketRequestAsync was successful for: {rpcId}");
            }
            else
            {
                Debug.Log($"SocketRequestAsync was NOT successful for: {rpcId}");
                Socket.ReceivedMatchState -= OnReceived;
                return response;
            }

            for (float t = 0f; (t < timeout) && !response.IsComplete; t += Time.deltaTime)
                await Task.Yield();

            Socket.ReceivedMatchState -= OnReceived;

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

        private async ValueTask<SocketResponse<ClaimPersonaReceipt>> ClaimPersonaAsync(PersonaMsg msg, CancellationToken cancellation = default, float timeout = TimeoutDuration)
            => await SocketRequestAsync<ClaimPersonaReceipt>("nakama/claim-persona", msg.ToJson(), cancellation, timeout);
        
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
