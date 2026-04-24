using System;
using System.Collections.Generic;
using System.Linq;

namespace Zip2_Avalonia.Coder;

public class PpmByteCoder
{
    private const int _order = 4;
    private Dictionary<BytesKey, Dictionary<byte, int>> _model = new();
    private Dictionary<BytesKey, byte> _map = new();

    private class BytesKey
    {
        private readonly byte[] _bytes;
        private readonly int _hashCode;

        public BytesKey(byte[] bytes)
        {
            _bytes = bytes;
            _hashCode = ComputeHash(bytes);
        }

        public byte[] Value => _bytes;
        public int Length => _bytes.Length;

        private static int ComputeHash(byte[] bytes)
        {
            int hash = 17;
            foreach (byte b in bytes)
                hash = hash * 31 + b;
            return hash;
        }

        public override bool Equals(object obj)
        {
            if (obj is not BytesKey other) return false;
            if (_bytes.Length != other._bytes.Length) return false;
            for (int i = 0; i < _bytes.Length; i++)
                if (_bytes[i] != other._bytes[i]) return false;
            return true;
        }

        public override int GetHashCode() => _hashCode;
    }

    private BytesKey GetContext(byte[] input, int currentIndex)
    {
        int start = Math.Max(0, currentIndex - _order);
        int length = currentIndex - start;

        byte[] context = new byte[length];
        Array.Copy(input, start, context, 0, length);
        return new BytesKey(context);
    }

    private BytesKey UpdateContext(BytesKey oldContext, byte nextByte)
    {
        List<byte> newContext = new List<byte>();

        if (oldContext.Length >= _order)
        {
            for (int i = 1; i < _order; i++)
                newContext.Add(oldContext.Value[i]);
        }
        else
        {
            newContext.AddRange(oldContext.Value);
        }

        newContext.Add(nextByte);
        return new BytesKey(newContext.ToArray());
    }

    public byte[] Encode(byte[] input)
    {
        var encoded = new List<byte>();

        for (int i = 0; i < input.Length; i++)
        {
            BytesKey context = GetContext(input, i);
            byte nextByte = input[i];

            if (!_model.ContainsKey(context))
                _model[context] = new Dictionary<byte, int>();

            if (!_model[context].ContainsKey(nextByte))
                _model[context][nextByte] = 0;

            _model[context][nextByte]++;

            BytesKey key = new BytesKey(context.Value.Concat(new[] { nextByte }).ToArray());

            if (!_map.ContainsKey(key))
                _map[key] = nextByte;

            encoded.Add(_map[key]);
        }

        return encoded.ToArray();
    }

    public byte[] Decode(byte[] encoded)
    {
        var decoded = new List<byte>();
        BytesKey context = new BytesKey(new byte[0]);

        foreach (byte code in encoded)
        {
            var match = _map
                .Where(x => x.Value == code && StartsWith(x.Key.Value, context.Value))
                .Select(x => x.Key)
                .FirstOrDefault();

            if (match == null)
                throw new Exception($"Decode error: no matching context for code '{code}'");

            byte nextByte = match.Value[^1];
            decoded.Add(nextByte);
            context = UpdateContext(context, nextByte);
        }

        return decoded.ToArray();
    }

    private bool StartsWith(byte[] bytes, byte[] prefix)
    {
        if (prefix.Length > bytes.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (bytes[i] != prefix[i]) return false;
        return true;
    }

    public Dictionary<byte, int> ExportFrequency()
    {
        var totalFrequency = new Dictionary<byte, int>();

        foreach (var contextStats in _model)
        {
            foreach (var symbolCount in contextStats.Value)
            {
                byte symbol = symbolCount.Key;
                int count = symbolCount.Value;

                if (!totalFrequency.ContainsKey(symbol))
                    totalFrequency[symbol] = 0;

                totalFrequency[symbol] += count;
            }
        }

        return totalFrequency;
    }

    public PpmByteModelData ExportModel()
    {
        var serializableMap = new Dictionary<string, byte>();
        foreach (var kvp in _map)
        {
            string keyStr = Convert.ToBase64String(kvp.Key.Value);
            serializableMap[keyStr] = kvp.Value;
        }

        return new PpmByteModelData { Map = serializableMap };
    }

    public void ImportModel(PpmByteModelData data)
    {
        if (data?.Map == null) return;

        _map = new Dictionary<BytesKey, byte>();
        foreach (var kvp in data.Map)
        {
            byte[] keyBytes = Convert.FromBase64String(kvp.Key);
            _map[new BytesKey(keyBytes)] = kvp.Value;
        }
    }
}
