# Context 7 API
Use the Context7 API to search libraries and fetch documentation programmatically

## Search

```
curl -X GET "https://context7.com/api/v1/search?query=react+hook+form" \
  -H "Authorization: Bearer CONTEXT7_API_KEY"
```

Parameters
query - Search term for finding libraries

Response
```
{
  "results": [
    {
      "id": "/react-hook-form/documentation",
      "title": "React Hook Form",
      "description": "📋 Official documentation", 
      "totalTokens": 50275,
      "totalSnippets": 274,
      "stars": 741,
      "trustScore": 9.1,
      "versions": []
    },
    ...
  ]
}
```

## Get Docs

```
curl -X GET "https://context7.com/api/v1/vercel/next.js?type=txt&topic=ssr&tokens=5000" \
  -H "Authorization: Bearer CONTEXT7_API_KEY"
```

Parameters
type - Response format (txt, json)
topic - Filter by topic
tokens - Token limit

Response Format: Text
```
TITLE: Dynamically Load Component Client-Side Only in Next.js Pages Router
DESCRIPTION: Explains how to disable Server-Side Rendering (SSR) for a dynamically...
SOURCE: https://github.com/vercel/next.js/blob/canary/docs/01-app/02-guides/lazy...

LANGUAGE: JSX
CODE:
```
'use client'

import dynamic from 'next/dynamic'

const DynamicHeader = dynamic(() => import('../components/header'), {
  ssr: false,
})
```

----------------------------------------

TITLE: Resolve `Math.random()` SSR Issues with React Suspense in Next.js
DESCRIPTION: This solution demonstrates how to wrap a Client Component that uses...
```

Response Format: Json
```
[
  {
    "codeTitle": "Configure Next.js for Server-Side Rendering",
    "codeDescription": "These snippets illustrate how to modify...",
    "codeLanguage": "diff",
    "codeTokens": 199,
    "codeId": "https://github.com/vercel/next.js/...",
    "pageTitle": "Items",
    "codeList": [
      {
        "language": "diff",
        "code": "--- a/package.json\n+++ b/package.json..."
      }
    ],
    "relevance": 0.016666668
  },
  ...
]
```