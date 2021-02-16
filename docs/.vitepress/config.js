module.exports = {
  base: '/v4/',
  lang: 'en-US',
  title: 'Marten',
  description: '.NET Transactional Document DB and Event Store on PostgreSQL',
  head: [
    ['link', { rel: 'apple-touch-icon', type: 'image/png', size: "180x180", href: '/apple-touch-icon.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "32x32", href: '/favicon-32x32.png' }],
    ['link', { rel: 'icon', type: 'image/png', size: "16x16", href: '/favicon-16x16.png' }],
    ['link', { rel: 'manifest', manifest: '/manifest.json' }]
  ],

  themeConfig: {
    logo: '/logo.png',
    repo: 'JasperFx/marten',
    docsDir: 'docs',
    docsBranch: 'master',
    editLinks: true,
    editLinkText: 'Suggest changes to this page',

    nav: [
      { text: 'v4.x',
        items: [
          { text: 'v3.x', link: 'https://martendb.io'}
        ]
      },
      { text: 'Guide', link: '/guide/' },
      { text: 'Migration', link: '/migration-guide' },
      { text: 'Release Notes', link: 'https://github.com/JasperFx/marten/releases' },
      { text: 'Gitter | Join Chat', link: 'https://gitter.im/jasperfx/marten?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge' },
      { text: 'Twitter', link: 'https://twitter.com/intent/follow?original_referer=https%3A%2F%2Fmartendb.io%2F&ref_src=twsrc%5Etfw&region=follow_link&screen_name=marten_lib&tw_p=followbutton' },
    ],

    algolia: {
      apiKey: 'your_api_key',
      indexName: 'index_name'
    },

    sidebar: [
      {
        text: 'Getting Started',
        link: '/guide/'
      },
      {
        text: 'Integration',
        link: '/guide/integration'
      },
      {
        text: 'Postgres Schema',
        link: '/guide/schema/',
        children: getMartenPGSchemaSidebar()
      },
      {
        text: 'Commandline Tooling',
        link: '/guide/cli'
      },
      {
        text: 'Document DB',
        link: '/guide/documents/',
        children: getDocumentDbSidebar()
      },
      {
        text: 'Event Store',
        link: '/guide/events/',
        children: getEventStoreSidebar()
      },
      {
        text: 'FAQ & Troubleshooting',
        link: '/guide/troubleshoot'
      },
      {
        text: 'Scenarios',
        link: '/guide/scenarios/',
        children: getScenariosSidebar()
      },
      {
        text: 'Managing Postgres Instance',
        link: '/guide/admin/',
        children: getManagingPGSidebar()
      },
      {
        text: 'Postgres for SQL Server users',
        link: '/guide/postgres/',
        children: getPgForSqlServerUsersSidebar()
      }
    ]
  }
}

function getMartenPGSchemaSidebar() {
  return [
    {
      text: 'How documents are stored',
      link: '/guide/schema/storage'
    },
    {
      text: 'Metadata',
      link: '/guide/schema/metadata'
    },
    {
      text: 'Schema',
      link: '/guide/schema/schema'
    },
    {
      text: 'Schema Migrations',
      link: '/guide/schema/migrations'
    },
    {
      text: 'Exporting Schema Definition',
      link: '/guide/schema/exporting'
    }
  ]
}

function getDocumentDbSidebar() {
  return [
    {
      text: 'Document identity',
      link: '/guide/documents/identity/',
      children: [
        {
          text: 'GUID identifiers',
          link: '/guide/documents/identity/guid'
        },
        {
          text: 'Sequential Identifiers with Hilo',
          link: '/guide/documents/identity/sequential'
        },
        {
          text: 'Custom identity strategies',
          link: '/guide/documents/identity/custom'
        },
      ]
    },
    {
      text: 'Document basics',
      link: '/guide/documents/basics/',
      children: [
        {
          text: 'Storing Documents and Unit Of Work',
          link: '/guide/documents/basics/persisting'
        },
        {
          text: 'Loading documents',
          link: '/guide/documents/basics/loading'
        },
        {
          text: 'Bulk insert documents',
          link: '/guide/documents/basics/bulk-insert'
        },
        {
          text: 'Initial data',
          link: '/guide/documents/basics/initial-data'
        }
      ]
    },
    {
      text: 'Querying',
      link: '/guide/documents/querying/',
      children: [
        {
          text: 'Querying document with Linq',
          link: '/guide/documents/querying/linq'
        },
        {
          text: 'Asynchronous querying',
          link: '/guide/documents/querying/async'
        },
        {
          text: 'Querying with Postgres SQL',
          link: '/guide/documents/querying/sql'
        },
        {
          text: 'Document projections',
          link: '/guide/documents/querying/projections'
        },
        {
          text: 'Query for raw JSON',
          link: '/guide/documents/querying/query-json'
        },
        {
          text: 'Including related documents',
          link: '/guide/documents/querying/include'
        },
        {
          text: 'Compiled queries',
          link: '/guide/documents/querying/compiled-queries'
        },
        {
          text: 'Batched queries',
          link: '/guide/documents/querying/batched-queries'
        },
        {
          text: 'Metadata queries',
          link: '/guide/documents/querying/metadata-queries'
        },
        {
          text: 'Paging',
          link: '/guide/documents/querying/paging'
        }
      ]
    },
    {
      text: 'Tenancy',
      link: '/guide/documents/tenancy/',
      children: [
        {
          text: 'Configuring tenancy',
          link: '/guide/documents/tenancy/configuring'
        },
        {
          text: 'Basic operations',
          link: '/guide/documents/tenancy/basic-operations'
        },
        {
          text: 'Implementation details',
          link: '/guide/documents/tenancy/implementation-details'
        }
      ]
    },
    {
      text: 'Configuration',
      link: '/guide/documents/configuration/',
      children: [
        {
          text: 'Calculated index',
          link: '/guide/documents/configuration/computed-indexes'
        },
        {
          text: 'Document policies',
          link: '/guide/documents/configuration/document-policies'
        },
        {
          text: 'Duplicated fields',
          link: '/guide/documents/configuration/duplicated-fields'
        },
        {
          text: 'Foreign keys',
          link: '/guide/documents/configuration/foreign-keys'
        },
        {
          text: 'Full text indexes',
          link: '/guide/documents/configuration/full-text'
        },
        {
          text: 'GIN or GiST indexes',
          link: '/guide/documents/configuration/gin-gist-indexes'
        },
        {
          text: 'Metadata indexes',
          link: '/guide/documents/configuration/metadata-indexes'
        },
        {
          text: 'Noda Time support',
          link: '/guide/documents/configuration/noda-time'
        },
        {
          text: 'Unique indexes',
          link: '/guide/documents/configuration/unique'
        },
      ]
    },
    {
      text: 'JSON serialization',
      link: '/guide/documents/json/',
      children: [
        {
          text: 'Newtonsoft.Json',
          link: '/guide/documents/json/newtonsoft'
        },
        {
          text: 'Jil',
          link: '/guide/documents/json/jil'
        }
      ]
    },
    {
      text: 'Advanced topics',
      link: '/guide/documents/advanced/',
      children: [
        {
          text: 'Tearing down document storage',
          link: '/guide/documents/advanced/cleaning'
        },
        {
          text: 'Extend Linq support',
          link: '/guide/documents/advanced/customizing-linq'
        },
        {
          text: 'Ejecting documents from session',
          link: '/guide/documents/advanced/eject'
        },
        {
          text: 'Enlisting in existing transactions',
          link: '/guide/documents/advanced/existing-transactions'
        },
        {
          text: 'Schema feature extensions',
          link: '/guide/documents/advanced/extensions'
        },
        {
          text: 'Document hierarchies',
          link: '/guide/documents/advanced/hierarchies'
        },
        {
          text: 'Identity map mechanics',
          link: '/guide/documents/advanced/identity-map'
        },
        {
          text: 'Javascript transformations',
          link: '/guide/documents/advanced/javascript-transformations'
        },
        {
          text: 'Optimistic concurrency',
          link: '/guide/documents/advanced/optimistic-concurrency'
        },
        {
          text: 'Patch API',
          link: '/guide/documents/advanced/patch-api'
        },
        {
          text: 'Retry Policies',
          link: '/guide/documents/advanced/retry-policy'
        },
        {
          text: 'Soft deletes',
          link: '/guide/documents/advanced/soft-deletes'
        },
        {
          text: 'Structural typing',
          link: '/guide/documents/advanced/structural-typing'
        }
      ]
    },
    {
      text: 'Diagnostics and instrumentation',
      link: '/guide/documents/diagnostics'
    },
    {
      text: 'Command timeouts',
      link: '/guide/documents/command-timeouts'
    },
    {
      text: 'Connection handling',
      link: '/guide/documents/connections'
    }
  ]
}

function getEventStoreSidebar() {
  return [
    {
      text: 'Schema Objects',
      link: '/guide/events/schema'
    },
    {
      text: 'Stream identity',
      link: '/guide/events/identity'
    },
    {
      text: 'Appending events',
      link: '/guide/events/appending'
    },
    {
      text: 'Querying events and stream data',
      link: '/guide/events/streams'
    },
    {
      text: 'Projections',
      link: '/guide/events/projections/',
      children: [
        {
          text: 'Async Daemon',
          link: '/guide/events/projections/async-daemon'
        },
        {
          text: 'Projecting by event type',
          link: '/guide/events/projections/projection-by-event-type'
        },
        {
          text: 'Custom projections',
          link: '/guide/events/projections/custom'
        },
      ]
    },
    {
      text: 'Event versioning',
      link: '/guide/events/versioning'
    }
  ]
}

function getScenariosSidebar() {
  return [
    {
      text: 'Aggregates, events and repositories',
      link: '/guide/scenarios/aggregates-events-repositories'
    },
    {
      text: 'Copy and transform stream',
      link: '/guide/scenarios/copy-and-transform-stream'
    },
    {
      text: 'Dynamic data',
      link: '/guide/scenarios/dynamic-data'
    },
    {
      text: 'Immutable projections as read model',
      link: '/guide/scenarios/immutable-projections-read-model'
    },
    {
      text: 'Using sequences for unique identifiers',
      link: '/guide/scenarios/using-sequence-for-unique-id'
    },
  ]
}

function getPgForSqlServerUsersSidebar() {
  return [
    {
      text: 'Naming conventions',
      link: '/guide/postgres/naming'
    },
    {
      text: 'Escaping',
      link: '/guide/postgres/escaping'
    },
    {
      text: 'Types',
      link: '/guide/postgres/types'
    },
    {
      text: 'Casting',
      link: '/guide/postgres/casting'
    },
    {
      text: 'Casing',
      link: '/guide/postgres/casing/',
      children: [
        {
          text: 'Unique values',
          link: '/guide/postgres/casing/unique-values'
        },
        {
          text: 'Case insensitive data',
          link: '/guide/postgres/casing/case-insensitive-data'
        },
        {
          text: 'Queries',
          link: '/guide/postgres/casing/queries'
        },
        {
          text: 'Using duplicate fields',
          link: '/guide/postgres/casing/using-duplicate-fields'
        },
      ]
    },
    {
      text: 'Slow queries',
      link: '/guide/postgres/slow-queries'
    },
    {
      text: 'Indexing',
      link: '/guide/postgres/indexing'
    },
    {
      text: 'Indexing JSONB',
      link: '/guide/postgres/indexing-jsonb'
    },
    {
      text: 'If statements',
      link: '/guide/postgres/if-statements'
    },
    {
      text: 'Working with dates',
      link: '/guide/postgres/dates'
    },
    {
      text: 'Backup and restore',
      link: '/guide/postgres/backup-restore/',
      children: [
        {
          text: 'Local',
          link: '/guide/postgres/backup-restore/local'
        },
        {
          text: 'Remote',
          link: '/guide/postgres/backup-restore/remote'
        }
      ]
    },
  ]
}

function getManagingPGSidebar() {
  return [
    {
      text: 'Installing plv8 on Windows',
      link: '/guide/admin/installing-plv8-windows'
    }
  ]
}
