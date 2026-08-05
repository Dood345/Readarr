import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import styles from './ProtocolLabel.css';

function ProtocolLabel({ protocol }) {
  // The API sends the protocol type name now (UsenetDownloadProtocol), not a lowercase enum name,
  // so strip the suffix before looking up the CSS class - styles[protocol] would be undefined and
  // the label would render unstyled.
  const strippedName = protocol.replace('DownloadProtocol', '').toLowerCase();
  const protocolName = strippedName === 'usenet' ? 'nzb' : strippedName;

  return (
    <Label className={styles[strippedName]}>
      {protocolName}
    </Label>
  );
}

ProtocolLabel.propTypes = {
  protocol: PropTypes.string.isRequired
};

export default ProtocolLabel;
