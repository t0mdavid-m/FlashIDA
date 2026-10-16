using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Flash.Tests
{
    /// <summary>
    /// Key paths of method.json, read two ways, for the key documentation drift guard (ADR-0058).
    /// Path grammar: dotted keys; "*" is an entry of a map whose values are objects; "[]" is an item
    /// of an array whose items are objects. A map of scalars, a scalar array and an object with no
    /// documented children are one key each.
    /// </summary>
    internal static class KeyPaths
    {
        internal sealed class Reference
        {
            public readonly SortedSet<string> Paths = new SortedSet<string>(StringComparer.Ordinal);
            /// <summary>Paths whose reference value was null or an empty container: their schema
            /// subtree has no instance here to compare against.</summary>
            public readonly List<string> Uncovered = new List<string>();
        }

        private const string DefPrefix = "#/$defs/";

        public static Dictionary<string, Dictionary<string, object>> FromSchema(Dictionary<string, object> schema)
        {
            var defs = Defs(schema);
            var result = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            WalkSchema(schema, "", defs, result, 0);
            return result;
        }

        public static Reference FromReference(Dictionary<string, object> reference, Dictionary<string, object> schema)
        {
            var r = new Reference();
            WalkReference(reference, "", schema, Defs(schema), r, 0);
            return r;
        }

        public static List<string> Undocumented(Reference r, ICollection<string> schemaPaths)
        {
            return r.Paths.Where(p => !schemaPaths.Contains(p)).ToList();
        }

        public static List<string> Absent(Reference r, ICollection<string> schemaPaths)
        {
            return schemaPaths
                .Where(p => !r.Paths.Contains(p) && !r.Uncovered.Any(u => IsUnder(p, u)))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        private static bool IsUnder(string path, string prefix)
        {
            return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.Ordinal)
                && (path[prefix.Length] == '.' || path[prefix.Length] == '[');
        }

        private static Dictionary<string, object> Defs(Dictionary<string, object> schema)
        {
            object d;
            return schema.TryGetValue("$defs", out d) ? (Dictionary<string, object>)d : new Dictionary<string, object>();
        }

        private static string Join(string path, string key)
        {
            return path.Length == 0 ? key : path + "." + key;
        }

        /// <summary>Follows a $ref to its $defs target. The propertyNames.enum of the ref site
        /// (if any) is returned as the set of keys this use admits.</summary>
        private static Dictionary<string, object> Resolve(Dictionary<string, object> node,
            Dictionary<string, object> defs, out HashSet<string> admitted, int depth)
        {
            admitted = null;
            if (node == null) return null;
            if (depth > 32) throw new InvalidOperationException("$ref nesting deeper than 32");
            object pn, en;
            var pnd = node.TryGetValue("propertyNames", out pn) ? pn as Dictionary<string, object> : null;
            if (pnd != null && pnd.TryGetValue("enum", out en))
                admitted = new HashSet<string>(((IList)en).Cast<object>().Select(o => (string)o), StringComparer.Ordinal);
            object r;
            if (!node.TryGetValue("$ref", out r)) return node;
            var target = (string)r;
            if (!target.StartsWith(DefPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Unsupported $ref " + target);
            object def;
            if (!defs.TryGetValue(target.Substring(DefPrefix.Length), out def))
                throw new InvalidOperationException("Dangling $ref " + target);
            HashSet<string> inner;
            var resolved = Resolve((Dictionary<string, object>)def, defs, out inner, depth + 1);
            if (admitted == null) admitted = inner;
            return resolved;
        }

        private static Dictionary<string, object> Child(Dictionary<string, object> node, string key)
        {
            object v;
            return node != null && node.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static bool HasProperties(Dictionary<string, object> resolved)
        {
            return Child(resolved, "properties") != null;
        }

        /// <summary>Value schema of a user-keyed map, or null when the node is not a map.</summary>
        private static Dictionary<string, object> MapValue(Dictionary<string, object> resolved)
        {
            if (resolved == null || HasProperties(resolved)) return null;
            return Child(resolved, "additionalProperties");
        }

        private static void WalkSchema(Dictionary<string, object> node, string path, Dictionary<string, object> defs,
            Dictionary<string, Dictionary<string, object>> result, int depth)
        {
            HashSet<string> admitted;
            var resolved = Resolve(node, defs, out admitted, depth);
            var props = Child(resolved, "properties");
            if (props != null)
            {
                foreach (var kv in props)
                {
                    if (admitted != null && !admitted.Contains(kv.Key)) continue;
                    var p = Join(path, kv.Key);
                    var child = (Dictionary<string, object>)kv.Value;
                    result[p] = child;
                    WalkSchema(child, p, defs, result, depth + 1);
                }
                return;
            }
            HashSet<string> ignored;
            var mapValue = MapValue(resolved);
            if (mapValue != null && HasProperties(Resolve(mapValue, defs, out ignored, depth + 1)))
            {
                result[path + ".*"] = null;
                WalkSchema(mapValue, path + ".*", defs, result, depth + 1);
                return;
            }
            var items = Child(resolved, "items");
            if (items != null && HasProperties(Resolve(items, defs, out ignored, depth + 1)))
            {
                result[path + "[]"] = null;
                WalkSchema(items, path + "[]", defs, result, depth + 1);
            }
        }

        private static void WalkReference(object value, string path, Dictionary<string, object> schemaNode,
            Dictionary<string, object> defs, Reference r, int depth)
        {
            HashSet<string> ignored;
            var resolved = schemaNode == null ? null : Resolve(schemaNode, defs, out ignored, depth);
            if (value == null) { r.Uncovered.Add(path); return; }

            var dict = value as Dictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { r.Uncovered.Add(path); return; }
                var mapValue = MapValue(resolved);
                if (mapValue != null)
                {
                    if (!HasProperties(Resolve(mapValue, defs, out ignored, depth + 1))) return; // map of scalars: one key
                    foreach (var kv in dict)
                    {
                        r.Paths.Add(path + ".*");
                        WalkReference(kv.Value, path + ".*", mapValue, defs, r, depth + 1);
                    }
                    return;
                }
                var props = Child(resolved, "properties");
                foreach (var kv in dict)
                {
                    var p = Join(path, kv.Key);
                    r.Paths.Add(p);
                    WalkReference(kv.Value, p, Child(props, kv.Key), defs, r, depth + 1);
                }
                return;
            }

            var list = value as IList;
            if (list != null && !(value is string))
            {
                var items = Child(resolved, "items");
                bool objectItems = items != null && HasProperties(Resolve(items, defs, out ignored, depth + 1));
                if (list.Count == 0) { if (objectItems) r.Uncovered.Add(path); return; }
                if (!(list[0] is Dictionary<string, object>)) return; // scalar array: one key
                foreach (var item in list)
                {
                    r.Paths.Add(path + "[]");
                    WalkReference(item, path + "[]", items, defs, r, depth + 1);
                }
            }
        }
    }
}
