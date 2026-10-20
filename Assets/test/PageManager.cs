//简单的page管理
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PageManager 
{
    private static PageManager _inst;
    static PageManager Inst
    {
        get
        {
            if(_inst == null)
            {
                _inst = new PageManager();
            }

            return _inst;
        }
    }

    PageManager()
    {
        
    }

    void Init()
    {
        
    }

    void Alloc()
    {
        
    }
     
}
