namespace Internal.Runtime
{

    internal unsafe partial struct MethodTable
    {
        internal MethodTable* GetArrayEEType()
        {
            return MethodTable.Of<Array>();
        }

    }
}
