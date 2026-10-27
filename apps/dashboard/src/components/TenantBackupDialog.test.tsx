import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import '@testing-library/jest-dom';
import { beforeEach, describe, expect, test, vi } from 'vitest';
import { TenantBackupDialog } from './TenantBackupDialog';
import { api } from '../api/client';

// CR-15: backups are encrypted with a passphrase chosen in the dialog.
describe('Tenant backup dialog', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const renderDialog = () => render(
    <TenantBackupDialog tenantId="t-1" tenantName="Demo school" onClose={vi.fn()} onRestored={vi.fn()} />
  );

  test('download needs a matching passphrase of 12+ characters and sends it', async () => {
    const download = vi.spyOn(api, 'downloadTenantBackup').mockResolvedValue();
    renderDialog();
    const button = screen.getByRole('button', { name: /Download backup/ });
    const [pass, repeat] = screen.getAllByLabelText(/passphrase/i);

    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.change(pass, { target: { value: 'short' } });
    fireEvent.change(repeat, { target: { value: 'short' } });
    expect(button).toBeDisabled();

    fireEvent.change(pass, { target: { value: 'correct horse battery' } });
    fireEvent.change(repeat, { target: { value: 'correct horse batterY' } });
    expect(button).toBeDisabled();

    fireEvent.change(repeat, { target: { value: 'correct horse battery' } });
    expect(button).toBeEnabled();
    expect(pass).toHaveAttribute('type', 'password');

    fireEvent.click(button);
    await waitFor(() => expect(download).toHaveBeenCalledWith('t-1', 'correct horse battery'));
  });

  test('an encrypted backup asks for its passphrase before restore', async () => {
    const restore = vi.spyOn(api, 'restoreTenantBackup').mockResolvedValue({
      destinationTenantId: 't-1', sourceTenantId: 's-1', takenAtUtc: '2026-10-27T09:00:00Z',
      flowOsVersion: '1.2.2', flowOsCompatibility: 'same', workflowClasses: 2, workflowInstances: 3
    });
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Restore' }));
    const encrypted = JSON.stringify({
      format: 'flowos-tenant-backup-encrypted', formatVersion: 1, flowOsVersion: '1.2.2',
      takenAtUtc: '2026-10-27T09:00:00Z', ciphertext: 'AAAA'
    });
    const file = new File([encrypted], 'backup.encrypted.json', { type: 'application/json' });
    Object.defineProperty(file, 'text', { value: () => Promise.resolve(encrypted) });
    fireEvent.change(screen.getByLabelText(/Backup file/), { target: { files: [file] } });

    expect(await screen.findByText(/Encrypted backup/)).toBeInTheDocument();
    const restoreButton = screen.getByRole('button', { name: /Restore into this tenant/ });
    fireEvent.click(screen.getByRole('checkbox'));
    fireEvent.change(screen.getByLabelText(/Type RESTORE/), { target: { value: 'RESTORE' } });
    fireEvent.click(restoreButton);
    expect(restore).not.toHaveBeenCalled(); // no passphrase yet

    fireEvent.change(screen.getByLabelText(/^Backup passphrase/), { target: { value: 'correct horse battery' } });
    fireEvent.click(restoreButton);
    await waitFor(() => expect(restore).toHaveBeenCalledWith('t-1', encrypted, 'correct horse battery'));
  });
});
