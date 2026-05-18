#!/usr/bin/env node

import { Command } from 'commander';
import chalk from 'chalk';
import { MetasonicClient } from './metasonic-client';
import { normalizeResponseFieldList } from './field-list';
import {
  QUALIFIERS,
  QUALIFIER_NAMES,
  QUALIFIER_ID_MAP,
  MetasonicConfig,
  ResponseField,
} from './types';

const program = new Command();

program
  .name('metasonic-cli')
  .description('CLI tool for updating Metasonic DMS document qualifiers')
  .version('1.0.0');

// Global options
program
  .option('--env <environment>', 'Environment (test|live)', 'test')
  .option('--user <username>', 'Username (or METASONIC_USER)')
  .option('--pass <password>', 'Password (or METASONIC_PASSWORD)')
  .option('--db <database>', 'Database name (or METASONIC_DB)');

// Get command
program
  .command('get <docid>')
  .description('Get all qualifiers for a document')
  .action(async (docid: string, options: any) => {
    try {
      await handleGet(docid, getConfig(options));
    } catch (error) {
      console.error(chalk.red('Error:'), error instanceof Error ? error.message : String(error));
      process.exit(1);
    }
  });

// List command (alias for get)
program
  .command('list <docid>')
  .description('List all qualifiers (alias for get)')
  .action(async (docid: string, options: any) => {
    try {
      await handleGet(docid, getConfig(options));
    } catch (error) {
      console.error(chalk.red('Error:'), error instanceof Error ? error.message : String(error));
      process.exit(1);
    }
  });

// Update command
program
  .command('update <docid> <qualifier> <value>')
  .description('Update a single qualifier')
  .action(async (docid: string, qualifier: string, value: string, options: any) => {
    try {
      await handleUpdate(docid, qualifier, value, getConfig(options));
    } catch (error) {
      console.error(chalk.red('Error:'), error instanceof Error ? error.message : String(error));
      process.exit(1);
    }
  });

// Upload command (replace document file)
program
  .command('upload <docid> <filepath>')
  .description('Update document with a new file')
  .action(async (docid: string, filepath: string, options: any) => {
    try {
      await handleUpload(docid, filepath, getConfig(options));
    } catch (error) {
      console.error(chalk.red('Error:'), error instanceof Error ? error.message : String(error));
      process.exit(1);
    }
  });

// Help text
program.addHelpText('after', `

Common Qualifiers:
  kreditor    ${QUALIFIERS.KREDITOR}   Creditor/Vendor name
  belegnr     ${QUALIFIERS.BELEGNR}   Invoice/Document number
  betrag      ${QUALIFIERS.BETRAG}   Amount (net)
  sachlich    ${QUALIFIERS.SACHLICH}        Material verification (richtig/falsch/ungeprüft)
  preislich   ${QUALIFIERS.PREISLICH}        Price verification (richtig/falsch/ungeprüft)
  issplit     ${QUALIFIERS.IS_SPLIT}        Split document indicator (JA/NEIN)

Examples:
  $ metasonic-cli get 1527018
  $ metasonic-cli update 1527018 223 richtig
  $ metasonic-cli update 1527018 sachlich richtig
  $ metasonic-cli upload 1527018 ./invoice.pdf
  $ metasonic-cli update 1527018 sachlich richtig --env live
`);

// Parse arguments
program.parse();

/**
 * Get config from command options
 */
function getConfig(cmdObj: any): Partial<MetasonicConfig> {
  const opts = cmdObj.parent?.opts() || program.opts();

  return {
    environment: opts.env as 'test' | 'live',
    username: opts.user ?? process.env.METASONIC_USER,
    password: opts.pass ?? process.env.METASONIC_PASSWORD,
    database: opts.db ?? process.env.METASONIC_DB,
    endpoint: process.env.METASONIC_ENDPOINT,
  };
}

/**
 * Handle get command
 */
async function handleGet(docId: string, config: Partial<MetasonicConfig>): Promise<void> {
  console.log(chalk.blue(`Fetching document ${docId}...`));

  const client = new MetasonicClient(config);
  await client.connect();

  const document = await client.getDocument(docId);

  if (!document) {
    console.log(chalk.yellow('Document not found'));
    process.exit(1);
  }

  console.log();
  console.log(chalk.bold(`Document ID: ${document.ID}`));
  console.log(chalk.bold('Qualifiers:'));
  console.log('─'.repeat(80));
  console.log(
    chalk.gray(
      `${'ID'.padEnd(15)} ${'Name'.padEnd(20)} ${'Value'.padEnd(40)}`
    )
  );
  console.log('─'.repeat(80));

  const fields = normalizeResponseFieldList(document.FieldList);
  const nonEmptyFields = fields.filter((f: ResponseField) => f.Value && f.Value.trim() !== '');

  if (nonEmptyFields.length === 0) {
    console.log(chalk.yellow('No qualifiers found'));
  } else {
    for (const field of nonEmptyFields) {
      const name = QUALIFIER_NAMES[field.ID] || '';
      console.log(
        `${field.ID.padEnd(15)} ${name.padEnd(20)} ${field.Value.padEnd(40)}`
      );
    }
  }

  console.log();
  client.disconnect();
}

/**
 * Handle update command
 */
async function handleUpdate(
  docId: string,
  qualifierIdOrName: string,
  value: string,
  config: Partial<MetasonicConfig>
): Promise<void> {
  // Resolve qualifier ID from name if needed
  const qualifierId = resolveQualifierId(qualifierIdOrName);
  const qualifierName = QUALIFIER_NAMES[qualifierId] || '';

  console.log(chalk.blue(`Updating document ${docId}...`));
  console.log(`  Qualifier: ${qualifierId} ${qualifierName ? chalk.gray(`(${qualifierName})`) : ''}`);
  console.log(`  New value: ${chalk.green(value)}`);

  const client = new MetasonicClient(config);
  await client.connect();

  // Get current value
  const document = await client.getDocument(docId);
  if (!document) {
    console.log(chalk.red('Error: Document not found'));
    process.exit(1);
  }

  const currentField = normalizeResponseFieldList(document.FieldList).find(
    (f: ResponseField) => f.ID === qualifierId
  );
  if (currentField) {
    console.log(`  Current value: ${chalk.yellow(currentField.Value)}`);
  }

  // Update qualifier
  await client.updateQualifier(docId, qualifierId, value);
  console.log(chalk.green('✓ Update successful'));

  // Verify update
  const updatedDoc = await client.getDocument(docId);
  const updatedField = normalizeResponseFieldList(updatedDoc.FieldList).find(
    (f: ResponseField) => f.ID === qualifierId
  );
  if (updatedField) {
    console.log(`  Verified value: ${chalk.green(updatedField.Value)}`);
  }

  console.log();
  client.disconnect();
}

/**
 * Handle upload command
 */
async function handleUpload(
  docId: string,
  filePath: string,
  config: Partial<MetasonicConfig>
): Promise<void> {
  const fs = require('fs');
  const path = require('path');

  // Check if file exists
  if (!fs.existsSync(filePath)) {
    console.log(chalk.red(`Error: File not found: ${filePath}`));
    process.exit(1);
  }

  const fileName = path.basename(filePath);
  const fileSize = fs.statSync(filePath).size;
  const fileSizeKB = (fileSize / 1024).toFixed(2);

  console.log(chalk.blue(`Updating document ${docId} with new file...`));
  console.log(`  File: ${chalk.cyan(fileName)}`);
  console.log(`  Size: ${fileSizeKB} KB`);

  const client = new MetasonicClient(config);
  await client.connect();

  // Get current document info
  console.log(chalk.gray('  Fetching current document...'));
  const currentDoc = await client.getDocument(docId);
  console.log(chalk.gray(`  Current DocClass: ${currentDoc.DocClassID}`));

  // Upload new file
  console.log(chalk.gray('  Uploading new file...'));
  const result = await client.updateDocumentFile(docId, filePath);

  console.log(chalk.green('✓ Upload successful'));
  console.log(`  Document ID: ${chalk.green(result.ID)}`);

  if (result.ItemList && result.ItemList.length > 0) {
    console.log(`  File uploaded: ${result.ItemList[0].OriginalFileName}`);
  }

  console.log();
  client.disconnect();
}

/**
 * Resolve qualifier ID from friendly name or ID
 */
function resolveQualifierId(qualifierIdOrName: string): string {
  const lowerName = qualifierIdOrName.toLowerCase();
  return QUALIFIER_ID_MAP[lowerName] || qualifierIdOrName;
}
