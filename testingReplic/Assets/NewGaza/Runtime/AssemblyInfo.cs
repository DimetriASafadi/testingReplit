using System.Runtime.CompilerServices;

// Unity compiles Editor folders separately. Allow the native smoke checks to
// verify runtime internals without making those implementation details public.
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]