using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace FlagInjector;

sealed class FlogBank
{
    readonly MemEngine _mem;
    readonly NameResolver _resolver = new();
    readonly object _lk = new();

    long _oPointer   = 0x7e71128, _oToFlag  = 0x30,  _oToValue  = 0xc0;
    long _oNodeFwd   = 0x0,       _oNodeBwd = 0x8,   _oNodePair = 0x10;
    long _oPairKey   = 0x0,       _oPairValue = 0x30;
    long _oStrData   = 0x0,       _oStrSize = 0x10,  _oStrCap   = 0x18;
    long _oFirstNode = 0x0,       _oMapSize = 0x20;
    long _hashMapOff = 0x8;

    readonly Dictionary<string, long> _descMap = new();
    readonly List<string> _names = new();

    public bool Ready { get; private set; }
    public int Count { get { lock (_lk) return _descMap.Count; } }
    public IReadOnlyList<string> Names { get { lock (_lk) return _names.ToArray(); } }

    public event Action<string>? Log;

    public FlogBank(MemEngine mem) => _mem = mem;

    public void ApplyOffsets(Dictionary<string, long> offsets)
    {
        foreach (var kv in offsets)
            switch (kv.Key)
            {
                case "Pointer":      _oPointer   = kv.Value; break;
                case "ToFlag":       _oToFlag    = kv.Value; break;
                case "ToValue":      _oToValue   = kv.Value; break;
                case "NodeForward":  _oNodeFwd   = kv.Value; break;
                case "NodeBackward": _oNodeBwd   = kv.Value; break;
                case "NodePair":     _oNodePair  = kv.Value; break;
                case "Key":          _oPairKey   = kv.Value; break;
                case "Value":        _oPairValue = kv.Value; break;
                case "Data":         _oStrData   = kv.Value; break;
                case "Size":         _oStrSize   = kv.Value; break;
                case "Capacity":     _oStrCap    = kv.Value; break;
                case "FirstNode":    _oFirstNode = kv.Value; break;
                case "MapSize":      _oMapSize   = kv.Value; break;
                case "HashMapOff":   _hashMapOff = kv.Value; break;
            }
    }

    public bool Init()
    {
        lock (_lk)
        {
            _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false;
            if (!_mem.On) { Log?.Invoke("Bank: mem not attached"); return false; }

            long singleton = _mem.ReadPtr(_mem.Base + _oPointer);
            if (singleton < 0x10000) { Log?.Invoke($"Bank: bad singleton 0x{singleton:X}"); return false; }

            long mapBase  = singleton + _hashMapOff;
            long sentinel = mapBase;
            long firstNode = _mem.ReadPtr(mapBase + _oFirstNode);
            int  mapSize   = _mem.ReadInt32(mapBase + _oMapSize);

            if (firstNode < 0x10000)              { Log?.Invoke("Bank: bad firstNode"); return false; }
            if (mapSize <= 0 || mapSize > 100000) { Log?.Invoke($"Bank: suspect mapSize {mapSize}"); return false; }

            long node = firstNode;
            var visited = new HashSet<long>();
            int maxIter = Math.Min(mapSize + 100, 100000), count = 0;

            for (int i = 0; i < maxIter && node != 0 && node != sentinel; i++)
            {
                if (!visited.Add(node)) break;
                long pairAddr = node + _oNodePair;
                long keyAddr  = pairAddr + _oPairKey;
                string? key   = ReadMsvcString(keyAddr);
                if (key != null && key.Length > 0 && key.Length < 512)
                {
                    long descPtr = _mem.ReadPtr(pairAddr + _oPairValue);
                    if (descPtr > 0x10000 && !_descMap.ContainsKey(key))
                    {
                        _descMap[key] = descPtr;
                        _resolver.Add(key);
                        _names.Add(key);
                        count++;
                    }
                }
                node = _mem.ReadPtr(node + _oNodeFwd);
            }

            Ready = count > 0;
            Log?.Invoke($"Bank: {count} flags discovered");
            return Ready;
        }
    }

    string? ReadMsvcString(long addr)
    {
        var raw = _mem.ReadAbs(addr, 32);
        if (raw == null) return null;
        long size = BitConverter.ToInt64(raw, (int)_oStrSize);
        long cap  = BitConverter.ToInt64(raw, (int)_oStrCap);
        if (size < 0 || size > 4096 || cap < 0) return null;
        if (size == 0) return "";
        byte[]? strBytes;
        if (cap < 16)
        {
            if (size > 15) return null;
            strBytes = new byte[size];
            Array.Copy(raw, (int)_oStrData, strBytes, 0, (int)size);
        }
        else
        {
            long heapPtr = BitConverter.ToInt64(raw, (int)_oStrData);
            if (heapPtr < 0x10000) return null;
            strBytes = _mem.ReadAbs(heapPtr, (int)size);
            if (strBytes == null) return null;
        }
        try { return Encoding.UTF8.GetString(strBytes); } catch { return null; }
    }

    public string? Resolve(string name) { lock (_lk) { return Ready ? _resolver.Resolve(name) : null; } }
    public long GetValueAddr(string resolvedName) { lock (_lk) { return _descMap.TryGetValue(resolvedName, out long desc) ? desc + _oToValue : 0; } }
    public void Reset() { lock (_lk) { _descMap.Clear(); _resolver.Clear(); _names.Clear(); Ready = false; } }
}