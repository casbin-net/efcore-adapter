using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Casbin;
using Casbin.Util;

namespace Casbin.Adapter.EFCore.UnitTest
{
    public class TestUtil
    {
        internal List<T> AsList<T>(params T[] values)
        {
            return values.ToList();
        }
        internal List<string> AsList(params string[] values)
        {
            return values.ToList();
        }

        internal static void TestEnforce(Enforcer e, String sub, Object obj, String act, Boolean res)
        { 
            Assert.Equal(res, e.Enforce(sub, obj, act));
        }

        internal static void TestEnforceWithoutUsers(Enforcer e, String obj, String act, Boolean res)
        {
            Assert.Equal(res, e.Enforce(obj, act));
        }

        internal static void TestDomainEnforce(Enforcer e, String sub, String dom, String obj, String act, Boolean res)
        {
            Assert.Equal(res, e.Enforce(sub, dom, obj, act));
        }

        internal static void TestGetPolicy(Enforcer e, List<List<String>> res)
        {
            var myRes = e.GetPolicy();
            Assert.True(Array2DEquals(res, myRes));
        }

        internal static void TestGetFilteredPolicy(Enforcer e, int fieldIndex, List<List<String>> res, params string[] fieldValues)
        {
            var myRes = e.GetFilteredPolicy(fieldIndex, fieldValues);
            Assert.True(Array2DEquals(res, myRes));
        }

        internal static void TestGetGroupingPolicy(Enforcer e, List<List<String>> res)
        {
            var myRes = e.GetGroupingPolicy(); 
            Assert.Equal(res, myRes);
        }

        internal static void TestGetFilteredGroupingPolicy(Enforcer e, int fieldIndex, List<List<String>> res, params string[] fieldValues)
        {
            var myRes = e.GetFilteredGroupingPolicy(fieldIndex, fieldValues);
            Assert.Equal(res, myRes);
        }

        internal static void TestHasPolicy(Enforcer e, List<String> policy, Boolean res)
        {
            Boolean myRes = e.HasPolicy(policy); 
            Assert.Equal(res, myRes);
        }

        internal static void TestHasGroupingPolicy(Enforcer e, List<String> policy, Boolean res)
        {
            Boolean myRes = e.HasGroupingPolicy(policy);
            Assert.Equal(res, myRes);
        }

        internal static void TestGetRoles(Enforcer e, String name, List<String> res)
        {
            var myRes = e.GetRolesForUser(name);
            string message = "Roles for " + name + ": " + myRes + ", supposed to be " + res;
            Assert.True(SetEquals(res, myRes), message);
        }

        internal static void TestGetUsers(Enforcer e, String name, List<String> res)
        {
            var myRes = e.GetUsersForRole(name);
            var message = "Users for " + name + ": " + myRes + ", supposed to be " + res;
            Assert.True(SetEquals(res, myRes),message);
        }

        internal static void TestHasRole(Enforcer e, String name, String role, Boolean res)
        {
            Boolean myRes = e.HasRoleForUser(name, role);
            Assert.Equal(res, myRes);
        }

        internal static void TestGetPermissions(Enforcer e, String name, List<List<String>> res)
        {
            var myRes = e.GetPermissionsForUser(name);
            var message = "Permissions for " + name + ": " + myRes + ", supposed to be " + res;
            Assert.True(Array2DEquals(res, myRes));
        }

        internal static void TestHasPermission(Enforcer e, String name, List<String> permission, Boolean res)
        {
            Boolean myRes = e.HasPermissionForUser(name, permission);
            Assert.Equal(res, myRes);
        }

        internal static void TestGetRolesInDomain(Enforcer e, String name, String domain, List<String> res)
        {
            var myRes = e.GetRolesForUserInDomain(name, domain);
            var message = "Roles for " + name + " under " + domain + ": " + myRes + ", supposed to be " + res;
            Assert.True(SetEquals(res, myRes), message);
        }

        internal static void TestGetPermissionsInDomain(Enforcer e, String name, String domain, List<List<String>> res)
        {
            var myRes = e.GetPermissionsForUserInDomain(name, domain);
            Assert.True(Array2DEquals(res, myRes), "Permissions for " + name + " under " + domain + ": " + myRes + ", supposed to be " + res); 
        }

        public static bool ArrayEquals(IEnumerable<string> a, IEnumerable<string> b)
        {
            if(a is null && b is null || a.Count() == 0 && b.Count() == 0)
            {
                return true;
            }
            if (a.Count() != b.Count())
            {
                return false;
            }
            var enumeratorA = a.GetEnumerator();
            var enumeratorB = b.GetEnumerator();

            for (int i = 0; i < a.Count(); i++)
            {
                enumeratorA.MoveNext();
                enumeratorB.MoveNext();
                if(!enumeratorA.Current.Equals(enumeratorB.Current))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool Array2DEquals(IEnumerable<IEnumerable<string>> a, IEnumerable<IEnumerable<string>> b)
        {
            if(a is null && b is null || a.Count() == 0 && b.Count() == 0)
            {
                return true;
            }
            if (a.Count() != b.Count())
            {
                return false;
            }
            var enumeratorA = a.GetEnumerator();
            var enumeratorB = b.GetEnumerator();

            for (int i = 0; i < a.Count(); i++)
            {
                if(!ArrayEquals(enumeratorA.Current, enumeratorB.Current))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b)
        {
            if (a == null)
            {
                a = new List<string>();
            }
            if (b == null)
            {
                b = new List<string>();
            }
            if (a.Count() != b.Count())
            {
                return false;
            }
            return a.Intersect(b).Count() == b.Count();
        }
    }
}
