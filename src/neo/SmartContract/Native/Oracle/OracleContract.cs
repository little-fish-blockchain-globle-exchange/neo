#pragma warning disable IDE0051

using Neo.Cryptography;
using Neo.Ledger;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Neo.SmartContract.Native.Oracle
{
    public sealed class OracleContract : NativeContract
    {
        private const byte Prefix_RequestId = 9;
        private const byte Prefix_Request = 7;
        private const byte Prefix_IdList = 6;

        public override int Id => -4;
        public override string Name => "Oracle";

        [ContractMethod(0, CallFlags.AllowModifyStates)]
        private void Finish(ApplicationEngine engine)
        {
            //TODO: Check witness for oracle nodes.
            //TODO: The witnesses from the request tx should be copied to the response tx.
            Transaction tx = (Transaction)engine.ScriptContainer;
            OracleResponse response = tx.Attributes.OfType<OracleResponse>().First();
            StorageKey key = CreateStorageKey(Prefix_Request, BitConverter.GetBytes(response.Id));
            OracleRequest request = engine.Snapshot.Storages[key].GetInteroperable<OracleRequest>();
            engine.Snapshot.Storages.Delete(key);
            key = CreateStorageKey(Prefix_IdList, GetUrlHash(request.Url));
            IdList list = engine.Snapshot.Storages.GetAndChange(key).GetInteroperable<IdList>();
            if (!list.Remove(response.Id)) throw new InvalidOperationException();
            if (list.Count == 0) engine.Snapshot.Storages.Delete(key);
            engine.CallFromNativeContract(null, request.CallbackContract, request.CallbackMethod, request.Url, response.Result);
        }

        public IEnumerable<OracleRequest> GetRequests(StoreView snapshot)
        {
            return snapshot.Storages.Find(new byte[] { Prefix_Request }).Select(p => p.Value.GetInteroperable<OracleRequest>());
        }

        public IEnumerable<OracleRequest> GetRequestsByUrl(StoreView snapshot, string url)
        {
            IdList list = snapshot.Storages.TryGet(CreateStorageKey(Prefix_IdList, GetUrlHash(url)))?.GetInteroperable<IdList>();
            if (list is null) yield break;
            foreach (ulong id in list)
                yield return snapshot.Storages[CreateStorageKey(Prefix_Request, BitConverter.GetBytes(id))].GetInteroperable<OracleRequest>();
        }

        private static byte[] GetUrlHash(string url)
        {
            return Crypto.Hash160(Utility.StrictUTF8.GetBytes(url));
        }

        internal override void Initialize(ApplicationEngine engine)
        {
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_RequestId), new StorageItem(BitConverter.GetBytes(0ul)));
        }

        //TODO: We should check the price later.
        [ContractMethod(0, CallFlags.AllowModifyStates)]
        private void Request(ApplicationEngine engine, string url, string callback)
        {
            //TODO: The sender of this tx should pay for the response tx.
            //TODO: Limitations on the requests.
            StorageItem item_id = engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_RequestId));
            ulong id = BitConverter.ToUInt64(item_id.Value) + 1;
            item_id.Value = BitConverter.GetBytes(id);
            engine.Snapshot.Storages.Add(CreateStorageKey(Prefix_Request, item_id.Value), new StorageItem(new OracleRequest
            {
                Url = url,
                Txid = ((Transaction)engine.ScriptContainer).Hash,
                CallbackContract = engine.CallingScriptHash,
                CallbackMethod = callback
            }));
            engine.Snapshot.Storages.GetAndChange(CreateStorageKey(Prefix_IdList, GetUrlHash(url)), () => new StorageItem(new IdList())).GetInteroperable<IdList>().Add(id);
        }
    }
}