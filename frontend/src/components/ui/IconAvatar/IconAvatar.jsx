import { TrendUpIcon, TrendDownIcon } from "../icons/FeatureIcons.jsx";
import "./IconAvatar.css";

export default function IconAvatar({ type }) {
  const classes = ["icon-avatar", `icon-avatar--${type}`].join(" ");
  return <span className={classes}>{type === "income" ? <TrendUpIcon /> : <TrendDownIcon />}</span>;
}
