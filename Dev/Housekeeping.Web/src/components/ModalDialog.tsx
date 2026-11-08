import React, { useEffect, useId, useRef } from 'react';

interface ModalDialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  closedBy?: 'any' | 'closerequest' | 'none';
  size?: 'narrow' | 'normal' | 'wide';
  footer?: React.ReactNode;
  children: React.ReactNode;
}

export const ModalDialog: React.FC<ModalDialogProps> = ({
  open,
  onClose,
  title,
  closedBy = 'any',
  size = 'normal',
  footer,
  children,
}) => {
  const dialogRef = useRef<HTMLDialogElement | null>(null);
  const titleId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    if (open) {
      if (!dialog.open) {
        dialog.showModal();
      }
    } else if (dialog.open) {
      dialog.close();
    }
  }, [open]);

  // Progressive enhancement fallback for browsers lacking <dialog closedby="any">
  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog || closedBy !== 'any') return;
    if ('closedBy' in HTMLDialogElement.prototype) return;

    const handleClick = (event: MouseEvent) => {
      if (event.target !== dialog) return;
      const rect = dialog.getBoundingClientRect();
      const isDialogContent =
        rect.top <= event.clientY &&
        event.clientY <= rect.top + rect.height &&
        rect.left <= event.clientX &&
        event.clientX <= rect.left + rect.width;
      if (!isDialogContent) {
        dialog.close();
      }
    };

    dialog.addEventListener('click', handleClick);
    return () => dialog.removeEventListener('click', handleClick);
  }, [closedBy]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;

    const handleClose = () => {
      dialog.returnValue = '';
      onClose();
    };

    const handleCancel = (e: Event) => {
      if (closedBy === 'none') {
        e.preventDefault();
      }
    };

    dialog.addEventListener('close', handleClose);
    dialog.addEventListener('cancel', handleCancel);
    return () => {
      dialog.removeEventListener('close', handleClose);
      dialog.removeEventListener('cancel', handleCancel);
    };
  }, [closedBy, onClose]);

  const sizeClass = size === 'wide' ? 'wide' : size === 'narrow' ? 'narrow' : '';

  return (
    <dialog
      ref={dialogRef}
      className={`hk-dialog ${sizeClass}`.trim()}
      aria-labelledby={titleId}
      closedby={closedBy}
    >
      <div className="hk-dialog-header">
        <h2 id={titleId}>{title}</h2>
        {closedBy !== 'none' && (
          <button
            type="button"
            className="hk-btn sm ghost"
            aria-label="Close dialog"
            onClick={() => dialogRef.current?.close()}
          >
            ✕
          </button>
        )}
      </div>
      <div className="hk-dialog-body">{children}</div>
      {footer && <div className="hk-dialog-footer">{footer}</div>}
    </dialog>
  );
};
